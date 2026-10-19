using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Calculations;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Microsoft.Extensions.Logging;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// L'enregistrement d'un encaissement, en une transaction (numéro ENC réservé dans la même) :
/// <list type="number">
/// <item>contrôle des factures : même client, validées, reste dû suffisant ;</item>
/// <item>part de TVA sur encaissements rendue exigible, au prorata du reste dû de chaque facture ;</item>
/// <item>écriture du journal de banque : 512 au débit, 411 au crédit, écart en 658 ou 758, 44574 vers 44571 ;</item>
/// <item>entrée <see cref="JournalEntryKind.PaymentRecorded"/> au journal inaltérable ;</item>
/// <item>rapprochement de la ligne de relevé et mémorisation de l'IBAN du payeur (empreinte) ;</item>
/// <item>lettrage du groupe d'écritures si toutes ses factures sont soldées.</item>
/// </list>
/// Après l'enregistrement, l'encaissement est déclaré à la plateforme agréée pour chaque facture qui y a été
/// transmise ; un échec n'annule pas l'encaissement (la déclaration se refait depuis la facture).
/// </summary>
internal sealed partial class RecordPaymentHandler(
    IInvoiceRepository invoices,
    ICustomerRepository customers,
    IPaymentRepository payments,
    IBankJournal bankJournal,
    IBankStatementRepository statements,
    ILetteringRepository letterings,
    IBankingSettingsRepository settings,
    IKnownIbanStore knownIbans,
    IReceivableDocumentReader receivables,
    IDocumentNumbering numbering,
    IIntegrityJournal journal,
    IPlatformTransmissionStore transmissions,
    ICommandDispatcher commands,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<RecordPaymentHandler> logger) : ICommandHandler<RecordPaymentCommand, Result<PaymentResponse>>
{
    public async Task<Result<PaymentResponse>> HandleAsync(RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecordPaymentRequest request = command.Request ?? new RecordPaymentRequest();
        List<Error> errors = Precheck(request, LegalCalendar.Today(clock));
        if (errors.Count > 0)
        {
            return Result<PaymentResponse>.Failure(errors);
        }

        Customer? customer = await customers.FindAsync(new CustomerId(request.CustomerId), cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return PaymentErrors.CustomerNotFound;
        }

        decimal threshold = (await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? BankingSettings.Default()).WriteOffThreshold;
        PaymentId id = PaymentId.New();
        Result<PaymentId> recorded = await numbering.ExecuteWithNextNumberAsync(
            Payment.Series,
            (number, ct) => RecordWithNumberAsync(command.Dossier, request, id, number, customer.Name, threshold, ct),
            cancellationToken).ConfigureAwait(false);
        if (recorded.IsFailure)
        {
            return Result<PaymentResponse>.Failure(recorded.Errors);
        }

        int reported = await ReportToPlatformAsync(command.Dossier, request, cancellationToken).ConfigureAwait(false);
        Result<PaymentResponse> response = await PaymentViews.LoadAsync(recorded.Value, payments, bankJournal, letterings, cancellationToken).ConfigureAwait(false);
        return response.IsSuccess ? response.Value with { PlatformReports = reported } : response;
    }

    internal static List<Error> Precheck(RecordPaymentRequest request, DateOnly today)
    {
        List<Error> errors = [];
        if (request.Amount <= 0m || decimal.Round(request.Amount, 2) != request.Amount)
        {
            errors.Add(PaymentErrors.AmountInvalid);
        }

        if (request.Date == default || request.Date.Year < 2000 || request.Date > today)
        {
            errors.Add(PaymentErrors.DateInvalid);
        }

        if (!Enum.IsDefined(request.Method))
        {
            errors.Add(PaymentErrors.MethodInvalid);
        }

        if (request.Reference is { Length: > Payment.MaxReferenceLength })
        {
            errors.Add(PaymentErrors.ReferenceTooLong);
        }

        IReadOnlyList<PaymentAllocationRequest> allocations = request.Allocations ?? [];
        if (allocations.Count == 0)
        {
            errors.Add(PaymentErrors.AllocationsRequired);
        }
        else if (allocations.Count > Payment.MaxAllocations)
        {
            errors.Add(PaymentErrors.TooManyAllocations);
        }
        else
        {
            for (int i = 0; i < allocations.Count; i++)
            {
                if (allocations[i] is null || allocations[i].InvoiceId == Guid.Empty || allocations[i].Amount <= 0m
                    || decimal.Round(allocations[i].Amount, 2) != allocations[i].Amount)
                {
                    errors.Add(PaymentErrors.AllocationInvalid(i));
                }
            }

            if (allocations.Where(a => a is not null).GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1))
            {
                errors.Add(PaymentErrors.AllocationDuplicate);
            }
        }

        return errors;
    }

    private async Task<Result<PaymentId>> RecordWithNumberAsync(
        DossierId dossier, RecordPaymentRequest request, PaymentId id, string number, string customerName, decimal threshold, CancellationToken cancellationToken)
    {
        // Rejouable : tout est relu, le suivi des entités ayant été vidé par la numérotation.
        DateTimeOffset now = clock.GetUtcNow();
        CustomerId customerId = new(request.CustomerId);
        List<Error> errors = [];
        List<(Invoice Invoice, decimal Amount)> targets = [];
        for (int i = 0; i < request.Allocations.Count; i++)
        {
            PaymentAllocationRequest allocation = request.Allocations[i];
            Invoice? invoice = await invoices.FindAsync(new InvoiceId(allocation.InvoiceId), cancellationToken).ConfigureAwait(false);
            if (invoice is null || invoice.Number is null)
            {
                errors.Add(PaymentErrors.AllocationInvalid(i));
            }
            else if (invoice.CustomerId != customerId)
            {
                errors.Add(PaymentErrors.OtherCustomerInvoice(i));
            }
            else
            {
                targets.Add((invoice, allocation.Amount));
            }
        }

        if (errors.Count > 0)
        {
            return Result<PaymentId>.Failure(errors);
        }

        IReadOnlyDictionary<InvoiceId, IReadOnlyDictionary<decimal, decimal>> pending =
            await receivables.PendingVatAsync([.. targets.Select(t => t.Invoice.Id)], cancellationToken).ConfigureAwait(false);
        List<PaymentAllocation> allocations = [];
        List<PaymentVatShare> shares = [];
        foreach ((Invoice invoice, decimal amount) in targets)
        {
            decimal outstanding = invoice.OutstandingInclTax;
            Result registered = invoice.RegisterPayment(amount, user.UserId, now);
            if (registered.IsFailure)
            {
                return Result<PaymentId>.Failure(registered.Errors);
            }

            allocations.Add(new PaymentAllocation(invoice.Id, invoice.Number!, amount));
            if (pending.TryGetValue(invoice.Id, out IReadOnlyDictionary<decimal, decimal>? byRate))
            {
                shares.AddRange(VatOnReceipt.Share(byRate, outstanding, amount).Select(t => new PaymentVatShare(invoice.Id, t.VatRatePercent, t.Amount)));
            }
        }

        BankStatementLine? line = null;
        if (request.BankLineId is Guid lineId)
        {
            line = await statements.FindLineAsync(lineId, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return BankStatementErrors.LineNotFound;
            }

            if (line.Amount != request.Amount)
            {
                return BankStatementErrors.AmountMismatch;
            }
        }

        Result<Payment> created = Payment.Record(
            id, number, customerId, customerName, request.Date, request.Amount, (PaymentMethod)(int)request.Method, request.Reference,
            allocations, shares, threshold, line?.Id, user.UserId, now);
        if (created.IsFailure)
        {
            return Result<PaymentId>.Failure(created.Errors);
        }

        Payment payment = created.Value;
        if (line is not null)
        {
            Result matched = line.MatchTo(payment.Id, user.UserId, now);
            if (matched.IsFailure)
            {
                return Result<PaymentId>.Failure(matched.Errors);
            }

            if (line.CounterpartyIbanHash is { } ibanHash)
            {
                await knownIbans.RememberAsync(customerId, ibanHash, now, cancellationToken).ConfigureAwait(false);
            }
        }

        await payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);
        await bankJournal.AddAsync(PaymentViews.EntryOf(payment, reversal: false), cancellationToken).ConfigureAwait(false);
        Result<long> journaled = await journal.AppendAsync(PaymentViews.EventOf(payment, JournalEntryKind.PaymentRecorded), cancellationToken).ConfigureAwait(false);
        if (journaled.IsFailure)
        {
            return Result<PaymentId>.Failure(journaled.Errors);
        }

        Dictionary<Guid, decimal> outstandingNow = targets.ToDictionary(t => t.Invoice.Id.Value, t => t.Invoice.OutstandingInclTax);
        await PaymentLettering.LetterAsync(customerId, payment, outstandingNow, receivables, payments, letterings, user.UserId, now, cancellationToken).ConfigureAwait(false);
        return payment.Id;
    }

    private async Task<int> ReportToPlatformAsync(DossierId dossier, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        int reported = 0;
        foreach (PaymentAllocationRequest allocation in request.Allocations)
        {
            PlatformTransmission? transmission = await transmissions.FindLatestAsync(allocation.InvoiceId, cancellationToken).ConfigureAwait(false);
            if (transmission?.Reference is null || transmission.Status == PlatformStatusDto.Rejected || transmission.IsFinal)
            {
                continue;
            }

            decimal amount = Math.Min(allocation.Amount, transmission.RemainingToCollect);
            if (amount <= 0m)
            {
                continue;
            }

            Result<PlatformTransmissionResponse> sent = await commands.SendAsync(
                new ReportPaymentCommand(dossier, new InvoiceId(allocation.InvoiceId), request.Date, amount), cancellationToken).ConfigureAwait(false);
            if (sent.IsSuccess)
            {
                reported++;
            }
            else
            {
                LogNotReported(logger, string.Join(", ", sent.Errors.Select(e => e.Code)));
            }
        }

        return reported;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Encaissement non déclaré à la plateforme pour une facture : {Codes}")]
    private static partial void LogNotReported(ILogger logger, string codes);
}

/// <summary>La contre-passation d'un encaissement, en une transaction avec son entrée au journal inaltérable.</summary>
internal sealed class ReversePaymentHandler(
    IInvoiceRepository invoices,
    IPaymentRepository payments,
    IBankJournal bankJournal,
    IBankStatementRepository statements,
    ILetteringRepository letterings,
    IBillingTransaction transaction,
    IIntegrityJournal journal,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ReversePaymentCommand, Result<PaymentResponse>>
{
    public async Task<Result<PaymentResponse>> HandleAsync(ReversePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReversePaymentRequest request = command.Request ?? new ReversePaymentRequest();
        DateOnly today = LegalCalendar.Today(clock);
        if (request.Date is DateOnly asked && (asked > today || asked.Year < 2000))
        {
            return PaymentErrors.DateInvalid;
        }

        PaymentId id = new(command.PaymentId);
        Result done = await transaction.ExecuteAsync(token => ReverseAsync(id, request, today, token), cancellationToken).ConfigureAwait(false);
        return done.IsFailure
            ? Result<PaymentResponse>.Failure(done.Errors)
            : await PaymentViews.LoadAsync(id, payments, bankJournal, letterings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> ReverseAsync(PaymentId id, ReversePaymentRequest request, DateOnly today, CancellationToken cancellationToken)
    {
        Payment? payment = await payments.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result reversed = payment.Reverse(request.Date ?? today, request.Reason, user.UserId, now);
        if (reversed.IsFailure)
        {
            return reversed;
        }

        foreach (PaymentAllocation allocation in payment.Allocations)
        {
            Invoice? invoice = await invoices.FindAsync(allocation.InvoiceId, cancellationToken).ConfigureAwait(false);
            invoice?.CancelPayment(allocation.Amount, user.UserId, now);
        }

        if (payment.BankLineId is Guid lineId && await statements.FindLineAsync(lineId, cancellationToken).ConfigureAwait(false) is { } line)
        {
            line.Unmatch(user.UserId, now);
        }

        await bankJournal.AddAsync(PaymentViews.EntryOf(payment, reversal: true), cancellationToken).ConfigureAwait(false);

        // Délettrage avec trace : le lettrage reste en base, marqué ; l'encaissement se lettre avec sa contre-passation.
        IReadOnlyList<Lettering> existing = await letterings.ListForCustomerAsync(payment.CustomerId, cancellationToken).ConfigureAwait(false);
        foreach (Lettering lettering in existing.Where(l => l.IsActive && l.Contains(LetteredEntryKind.Payment, payment.Id.Value)))
        {
            lettering.Cancel(payment.ReversalReason!, user.UserId, now);
        }

        await letterings.AddAsync(
            Lettering.Create(
                Guid.CreateVersion7(),
                payment.CustomerId,
                existing.Count,
                payment.ReversalDate!.Value,
                [new LetteringItem(LetteredEntryKind.Payment, payment.Id.Value), new LetteringItem(LetteredEntryKind.PaymentReversal, payment.Id.Value)],
                user.UserId,
                now),
            cancellationToken).ConfigureAwait(false);

        Result<long> journaled = await journal.AppendAsync(PaymentViews.EventOf(payment, JournalEntryKind.PaymentReversed), cancellationToken).ConfigureAwait(false);
        return journaled.IsFailure ? Result.Failure(journaled.Errors) : Result.Success();
    }
}

internal sealed class GetPaymentHandler(IPaymentRepository payments, IBankJournal bankJournal, ILetteringRepository letterings)
    : IQueryHandler<GetPaymentQuery, Result<PaymentResponse>>
{
    public Task<Result<PaymentResponse>> HandleAsync(GetPaymentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return PaymentViews.LoadAsync(new PaymentId(query.PaymentId), payments, bankJournal, letterings, cancellationToken);
    }
}

internal sealed class ListPaymentsHandler(IPaymentRepository payments, ILetteringRepository letterings)
    : IQueryHandler<ListPaymentsQuery, Result<PagedResponse<PaymentSummary>>>
{
    public async Task<Result<PagedResponse<PaymentSummary>>> HandleAsync(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int page = Math.Max(1, query.Page);
        int size = Math.Clamp(query.PageSize, 1, 200);
        (IReadOnlyList<Payment> items, int total) = await payments.ListAsync(
            new PaymentListFilter(query.From, query.To, query.CustomerId is Guid c ? new CustomerId(c) : null, query.Search, page, size),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Lettering> active = await letterings.ListActiveContainingAsync([.. items.Select(p => p.Id.Value)], cancellationToken).ConfigureAwait(false);
        List<PaymentSummary> summaries =
        [
            .. items.Select(p => new PaymentSummary(
                p.Id.Value,
                p.Number,
                (PaymentStatusDto)(int)p.Status,
                p.CustomerId.Value,
                p.CustomerName,
                p.Date,
                p.Amount,
                (PaymentMethodDto)(int)p.Method,
                p.Reference,
                [.. p.Allocations.Select(a => a.InvoiceNumber)],
                PaymentViews.CodeOf(active, p.Id.Value))),
        ];
        return new PagedResponse<PaymentSummary>(summaries, page, size, total);
    }
}

internal sealed class GetOpenInvoicesHandler(IReceivableDocumentReader receivables, TimeProvider clock)
    : IQueryHandler<GetOpenInvoicesQuery, Result<IReadOnlyList<OpenInvoiceResponse>>>
{
    public async Task<Result<IReadOnlyList<OpenInvoiceResponse>>> HandleAsync(GetOpenInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = LegalCalendar.Today(clock);
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(
            query.CustomerId is Guid c ? new CustomerId(c) : null, cancellationToken).ConfigureAwait(false);
        List<OpenInvoiceResponse> open =
        [
            .. documents
                .Where(d => d.Kind != InvoiceKind.CreditNote && d.Outstanding > 0m)
                .OrderBy(d => d.DueDate ?? d.IssueDate).ThenBy(d => d.Number, StringComparer.Ordinal)
                .Select(d => new OpenInvoiceResponse(
                    d.InvoiceId.Value,
                    (Contracts.Billing.InvoiceKindDto)(int)d.Kind,
                    d.Number,
                    d.CustomerId.Value,
                    d.CustomerName,
                    d.IssueDate,
                    d.DueDate,
                    d.TotalInclTax,
                    d.CreditedInclTax,
                    d.PaidInclTax,
                    d.Outstanding,
                    d.DueDate is DateOnly due && due < today ? today.DayNumber - due.DayNumber : 0)),
        ];
        return open;
    }
}

/// <summary>Le lettrage automatique après un encaissement (règles : <see cref="LetteringPlanner"/>).</summary>
internal static class PaymentLettering
{
    /// <summary>
    /// Lettre le groupe de <paramref name="payment"/> si toutes ses factures sont soldées. <paramref name="outstandingNow"/>
    /// donne le reste dû des factures modifiées dans la transaction en cours, pas encore enregistrées.
    /// </summary>
    public static async Task LetterAsync(
        CustomerId customer,
        Payment payment,
        IReadOnlyDictionary<Guid, decimal> outstandingNow,
        IReceivableDocumentReader receivables,
        IPaymentRepository payments,
        ILetteringRepository letterings,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(customer, cancellationToken).ConfigureAwait(false);
        List<LetteringPlanner.LedgerInvoice> ledger =
        [
            .. documents.Select(d => new LetteringPlanner.LedgerInvoice(
                d.InvoiceId.Value,
                d.CreditedInvoiceId?.Value,
                d.IssueDate,
                outstandingNow.TryGetValue(d.InvoiceId.Value, out decimal current) ? current : d.Outstanding,
                d.Kind == InvoiceKind.CreditNote)),
        ];
        IReadOnlyList<Payment> active = await payments.ListActiveForCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        List<LetteringPlanner.LedgerPayment> ledgerPayments =
        [
            .. active.Where(p => p.Id != payment.Id).Append(payment)
                .Select(p => new LetteringPlanner.LedgerPayment(p.Id.Value, p.Date, [.. p.Allocations.Select(a => a.InvoiceId.Value)])),
        ];
        IReadOnlyList<Lettering> existing = await letterings.ListForCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        HashSet<LetteringItem> lettered = [.. existing.Where(l => l.IsActive).SelectMany(l => l.Items)];
        var plan = LetteringPlanner.Plan(payment.Id.Value, ledger, ledgerPayments, lettered);
        if (plan is { } group)
        {
            await letterings.AddAsync(Lettering.Create(Guid.CreateVersion7(), customer, existing.Count, group.Date, group.Items, userId, now), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Écritures, événement du journal inaltérable et vue d'un encaissement.</summary>
internal static class PaymentViews
{
    public static BankJournalEntry EntryOf(Payment payment, bool reversal) => new(
        payment.Id,
        reversal,
        payment.Number,
        reversal ? payment.ReversalDate!.Value : payment.Date,
        payment.CustomerId,
        payment.CustomerName,
        PaymentEntryCalculator.Compute(
            payment.Amount,
            payment.AllocatedAmount,
            payment.VatShares.GroupBy(s => s.VatRatePercent).Select(g => new VatTransfer(g.Key, g.Sum(s => s.Amount))),
            reversal));

    public static DocumentLifecycleEvent EventOf(Payment payment, JournalEntryKind kind) => new(
        kind,
        JournaledDocumentKind.Payment,
        payment.Id.Value,
        Payment.Series,
        payment.Number,
        kind == JournalEntryKind.PaymentReversed ? payment.ReversalDate!.Value : payment.Date,
        payment.CustomerId.Value,
        0m,
        0m,
        payment.Amount,
        "EUR",
        payment.ComputeFingerprint());

    public static string? CodeOf(IEnumerable<Lettering> active, Guid paymentId) =>
        active.Where(l => l.IsActive && l.Contains(LetteredEntryKind.Payment, paymentId)).OrderByDescending(l => l.CreatedAt).Select(l => l.Code).FirstOrDefault();

    public static async Task<Result<PaymentResponse>> LoadAsync(
        PaymentId id, IPaymentRepository payments, IBankJournal bankJournal, ILetteringRepository letterings, CancellationToken cancellationToken)
    {
        Payment? payment = await payments.FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        IReadOnlyList<BankJournalEntry> entries = await bankJournal.ForPaymentAsync(id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Lettering> active = await letterings.ListActiveContainingAsync([id.Value], cancellationToken).ConfigureAwait(false);
        static List<Contracts.Accounting.JournalLineResponse> Lines(BankJournalEntry? entry) =>
            entry is null ? [] : [.. entry.Lines.Select(l => new Contracts.Accounting.JournalLineResponse(l.Account, l.Debit, l.Credit, l.VatRatePercent))];
        return new PaymentResponse(
            payment.Id.Value,
            payment.Version,
            payment.Number,
            (PaymentStatusDto)(int)payment.Status,
            payment.CustomerId.Value,
            payment.CustomerName,
            payment.Date,
            payment.Amount,
            (PaymentMethodDto)(int)payment.Method,
            payment.Reference,
            payment.BankLineId,
            [.. payment.Allocations.Select(a => new PaymentAllocationResponse(a.InvoiceId.Value, a.InvoiceNumber, a.Amount))],
            payment.AllocatedAmount,
            payment.WriteOff,
            CodeOf(active, id.Value),
            Lines(entries.FirstOrDefault(e => !e.IsReversal)),
            Lines(entries.FirstOrDefault(e => e.IsReversal)),
            payment.ReversalDate,
            payment.ReversalReason,
            payment.CreatedAt);
    }
}
