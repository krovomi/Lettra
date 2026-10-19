using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features;
using Lettra.Calculations;
using Lettra.Contracts.Common;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Application.Reminders;

/// <summary>Une relance écrite, avec son client et son signataire.</summary>
public sealed record PreparedReminder(ReminderDraft Draft, ReminderCustomer Customer, ReminderSender Sender);

/// <summary>Une facture échue vue par les relances, avec ce qu'il faut pour écrire la suivante.</summary>
internal sealed record OverdueInvoice(ReceivableDocument Document, ReminderCustomer Customer, int DaysOverdue, PaymentReminder? Last, decimal? PenaltyRate)
{
    public DateOnly DueDate => Document.DueDate!.Value;
}

/// <summary>
/// Les relances d'un dossier : quelles factures sont échues (le reste dû tient compte des encaissements et des avoirs),
/// quel niveau leur envoyer, le texte dans la langue du client, l'envoi par le port e-mail et la trace de chaque envoi.
/// Partagé par les cas d'usage de l'écran et par la passe quotidienne du Worker.
/// </summary>
public sealed class PaymentReminderService(
    IReceivableDocumentReader receivables,
    IReminderContextReader context,
    IPaymentReminderRepository reminders,
    IReminderSettingsRepository settingsStore,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public bool EmailConfigured => email.IsConfigured;

    public DateOnly Today => LegalCalendar.Today(clock);

    public async Task<ReminderSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await settingsStore.FindAsync(cancellationToken).ConfigureAwait(false) ?? ReminderSettings.Default();

    public ReminderSettingsResponse Describe(ReminderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new(settings.AutoSend, [settings.FriendlyDays, settings.ReminderDays, settings.FinalDays, settings.FormalNoticeDays],
            settings.MinimumGapDays, email.IsConfigured, settings.Version);
    }

    public async Task<ReminderOverviewResponse> OverviewAsync(CancellationToken cancellationToken)
    {
        ReminderSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);
        DateOnly today = Today;
        IReadOnlyList<OverdueInvoice> overdue = await OverdueAsync(today, cancellationToken).ConfigureAwait(false);
        List<ReminderInvoiceResponse> rows = [.. overdue.Select(o => Describe(o, settings, today))];
        return new ReminderOverviewResponse(rows, Describe(settings), rows.Count(r => r.DueToday));
    }

    /// <summary>La prochaine relance d'une facture échue, écrite dans la langue du client.</summary>
    public async Task<Result<PreparedReminder>> DraftAsync(InvoiceId invoice, CancellationToken cancellationToken)
    {
        OverdueInvoice? target = (await OverdueAsync(Today, cancellationToken).ConfigureAwait(false)).FirstOrDefault(o => o.Document.InvoiceId == invoice);
        if (target is null)
        {
            return ReminderErrors.InvoiceNotOverdue;
        }

        if (ReminderPlanner.Next(target.Last?.Level) is not ReminderLevel level)
        {
            return ReminderErrors.NoLevelLeft;
        }

        ReminderSender? sender = await context.SenderAsync(cancellationToken).ConfigureAwait(false);
        return sender is null ? ReminderErrors.CompanyProfileMissing : new PreparedReminder(Write(target, level, sender), target.Customer, sender);
    }

    /// <summary>
    /// Envoie (ou note) la prochaine relance d'une facture et la garde dans l'historique. Un e-mail refusé est gardé
    /// comme échec et répond <c>Email.SendFailed</c> ; un client sans adresse, ou une installation sans envoi d'e-mails,
    /// ne laisse aucune trace : rien n'a été tenté.
    /// </summary>
    public async Task<Result<PaymentReminder>> SendAsync(InvoiceId invoice, ReminderChannel channel, ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        Result<PreparedReminder> drafted = await DraftAsync(invoice, cancellationToken).ConfigureAwait(false);
        return drafted.IsFailure
            ? Result<PaymentReminder>.Failure(drafted.Errors)
            : await DeliverAsync(drafted.Value, channel, trigger, userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>La passe des relances dues aujourd'hui : envoi par e-mail aux clients qui ont une adresse.</summary>
    public async Task<Result<RunRemindersResponse>> RunDueAsync(ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured)
        {
            return EmailErrors.NotConfigured;
        }

        ReminderSender? sender = await context.SenderAsync(cancellationToken).ConfigureAwait(false);
        if (sender is null)
        {
            return ReminderErrors.CompanyProfileMissing;
        }

        ReminderSettings settings = await SettingsAsync(cancellationToken).ConfigureAwait(false);
        DateOnly today = Today;
        int due = 0, sent = 0, failed = 0, withoutEmail = 0;
        foreach (OverdueInvoice invoice in await OverdueAsync(today, cancellationToken).ConfigureAwait(false))
        {
            if (ReminderPlanner.Due(State(invoice), settings, today) is not ReminderLevel level)
            {
                continue;
            }

            due++;
            if (invoice.Customer.Email is null)
            {
                withoutEmail++;
                continue;
            }

            Result<PaymentReminder> delivered = await DeliverAsync(new PreparedReminder(Write(invoice, level, sender), invoice.Customer, sender), ReminderChannel.Email, trigger, userId, cancellationToken).ConfigureAwait(false);
            if (delivered.IsSuccess)
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new RunRemindersResponse(due, sent, failed, withoutEmail);
    }

    private async Task<Result<PaymentReminder>> DeliverAsync(
        PreparedReminder prepared, ReminderChannel channel, ReminderTrigger trigger, Guid userId, CancellationToken cancellationToken)
    {
        (ReminderDraft draft, ReminderCustomer customer, ReminderSender sender) = prepared;
        string? recipient = null;
        string? errorCode = null;
        if (channel == ReminderChannel.Email)
        {
            if (customer.Email is null)
            {
                return ReminderErrors.CustomerEmailMissing;
            }

            if (!email.IsConfigured)
            {
                return EmailErrors.NotConfigured;
            }

            recipient = customer.Email;
            Result<Email> to = Email.Create(customer.Email);
            Result result = to.IsFailure
                ? Result.Failure(to.Errors)
                : await email.SendAsync(new EmailMessage(to.Value, draft.Subject, draft.Body, SenderName: sender.Name), cancellationToken).ConfigureAwait(false);
            errorCode = result.IsFailure ? result.Errors[0].Code : null;
        }

        PaymentReminder reminder = PaymentReminder.Record(draft, channel, recipient, errorCode, trigger, userId, clock.GetUtcNow(), Today);
        await reminders.AddAsync(reminder, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return errorCode is null ? reminder : EmailErrors.SendFailed;
    }

    private async Task<IReadOnlyList<OverdueInvoice>> OverdueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        List<ReceivableDocument> documents =
        [
            .. (await receivables.ListAsync(null, cancellationToken).ConfigureAwait(false))
                .Where(d => d.Kind != InvoiceKind.CreditNote && d.Outstanding > 0m && d.DueDate is DateOnly due && due < today),
        ];
        if (documents.Count == 0)
        {
            return [];
        }

        InvoiceId[] ids = [.. documents.Select(d => d.InvoiceId)];
        IReadOnlyDictionary<InvoiceId, PaymentReminder> last = await reminders.LastSentAsync(ids, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<InvoiceId, decimal> rates = await context.PenaltyRatesAsync(ids, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<CustomerId, ReminderCustomer> customers = await context
            .CustomersAsync([.. documents.Select(d => d.CustomerId).Distinct()], cancellationToken).ConfigureAwait(false);
        return
        [
            .. documents
                .Select(d => new OverdueInvoice(
                    d,
                    customers.GetValueOrDefault(d.CustomerId) ?? new ReminderCustomer(d.CustomerId, d.CustomerName, null, null, IsProfessional: true),
                    today.DayNumber - d.DueDate!.Value.DayNumber,
                    last.GetValueOrDefault(d.InvoiceId),
                    rates.TryGetValue(d.InvoiceId, out decimal rate) ? rate : null))
                .OrderByDescending(o => o.DaysOverdue)
                .ThenBy(o => o.Document.Number, StringComparer.Ordinal),
        ];
    }

    private static ReminderState State(OverdueInvoice invoice) => new(invoice.DaysOverdue, invoice.Last?.Level, invoice.Last?.OccurredOn);

    private static LatePenalty Penalty(OverdueInvoice invoice) =>
        LatePenaltyCalculator.Compute(invoice.Document.Outstanding, invoice.PenaltyRate, invoice.DaysOverdue, invoice.Customer.IsProfessional);

    private static ReminderDraft Write(OverdueInvoice invoice, ReminderLevel level, ReminderSender sender)
    {
        string language = ReminderTemplates.LanguageOf(invoice.Customer.Language);
        ReceivableDocument d = invoice.Document;
        (string subject, string body) = ReminderTemplates.Write(new ReminderLetter(
            level, language, invoice.Customer.Name, d.Number, d.IssueDate, invoice.DueDate, d.Outstanding, invoice.DaysOverdue, invoice.PenaltyRate, Penalty(invoice), sender));
        return new ReminderDraft(d.InvoiceId, d.CustomerId, d.Number, level, language, subject, body, d.Outstanding, invoice.DaysOverdue);
    }

    private static ReminderInvoiceResponse Describe(OverdueInvoice o, ReminderSettings settings, DateOnly today)
    {
        LatePenalty penalty = Penalty(o);
        return new ReminderInvoiceResponse(
            o.Document.InvoiceId.Value,
            o.Document.Number,
            o.Document.CustomerId.Value,
            o.Customer.Name,
            o.Customer.Email,
            ReminderTemplates.LanguageOf(o.Customer.Language),
            o.Document.IssueDate,
            o.DueDate,
            o.Document.Outstanding,
            o.DaysOverdue,
            (ReminderLevelDto?)o.Last?.Level,
            o.Last?.OccurredAt,
            (ReminderLevelDto?)ReminderPlanner.Next(o.Last?.Level),
            ReminderPlanner.Due(State(o), settings, today) is not null,
            penalty.Interest,
            penalty.RecoveryIndemnity);
    }
}

internal static class ReminderMapping
{
    public static PaymentReminderResponse ToResponse(this PaymentReminder r, string customerName) => new(
        r.Id, r.InvoiceId.Value, r.InvoiceNumber, r.CustomerId.Value, customerName, (ReminderLevelDto)r.Level, (ReminderChannelDto)r.Channel,
        (ReminderOutcomeDto)r.Outcome, r.ErrorCode, r.Recipient, r.Language, r.Subject, r.Body, r.Outstanding, r.DaysOverdue,
        (ReminderTriggerDto)r.Trigger, r.OccurredAt);
}

internal sealed class GetReminderOverviewHandler(PaymentReminderService service) : IQueryHandler<GetReminderOverviewQuery, Result<ReminderOverviewResponse>>
{
    public async Task<Result<ReminderOverviewResponse>> HandleAsync(GetReminderOverviewQuery query, CancellationToken cancellationToken) =>
        await service.OverviewAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class GetReminderPreviewHandler(PaymentReminderService service) : IQueryHandler<GetReminderPreviewQuery, Result<ReminderPreviewResponse>>
{
    public async Task<Result<ReminderPreviewResponse>> HandleAsync(GetReminderPreviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Result<PreparedReminder> drafted = await service.DraftAsync(new InvoiceId(query.InvoiceId), cancellationToken).ConfigureAwait(false);
        return drafted.Map(d => new ReminderPreviewResponse(
            d.Draft.InvoiceId.Value, (ReminderLevelDto)d.Draft.Level, d.Draft.Language, d.Customer.Email, d.Draft.Subject, d.Draft.Body));
    }
}

internal sealed class SendReminderHandler(PaymentReminderService service, IReminderContextReader context, ICurrentUser user)
    : ICommandHandler<SendReminderCommand, Result<PaymentReminderResponse>>
{
    public async Task<Result<PaymentReminderResponse>> HandleAsync(SendReminderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReminderChannel channel = command.Request?.Channel == ReminderChannelDto.Manual ? ReminderChannel.Manual : ReminderChannel.Email;
        Result<PaymentReminder> sent = await service.SendAsync(new InvoiceId(command.InvoiceId), channel, ReminderTrigger.User, user.UserId, cancellationToken).ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return Result<PaymentReminderResponse>.Failure(sent.Errors);
        }

        IReadOnlyDictionary<CustomerId, ReminderCustomer> customers = await context.CustomersAsync([sent.Value.CustomerId], cancellationToken).ConfigureAwait(false);
        return sent.Value.ToResponse(customers.GetValueOrDefault(sent.Value.CustomerId)?.Name ?? "");
    }
}

internal sealed class RunDueRemindersHandler(PaymentReminderService service, ICurrentUser user) : ICommandHandler<RunDueRemindersCommand, Result<RunRemindersResponse>>
{
    public Task<Result<RunRemindersResponse>> HandleAsync(RunDueRemindersCommand command, CancellationToken cancellationToken) =>
        service.RunDueAsync(ReminderTrigger.User, user.UserId, cancellationToken);
}

internal sealed class ListPaymentRemindersHandler(IPaymentReminderRepository reminders)
    : IQueryHandler<ListPaymentRemindersQuery, Result<PagedResponse<PaymentReminderResponse>>>
{
    public const int MaxPageSize = Paging.MaxPageSize;

    public async Task<Result<PagedResponse<PaymentReminderResponse>>> HandleAsync(ListPaymentRemindersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int page = Math.Max(1, query.Page);
        int size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        ReminderHistoryFilter filter = new(
            query.InvoiceId is Guid i ? new InvoiceId(i) : null,
            query.CustomerId is Guid c ? new CustomerId(c) : null,
            (ReminderLevel?)query.Level,
            (ReminderOutcome?)query.Outcome,
            (ReminderChannel?)query.Channel,
            query.From,
            query.To,
            ListSearch.Normalize(query.Search),
            query.Sort,
            query.Descending,
            page,
            size);
        (IReadOnlyList<ReminderHistoryItem> items, int total) = await reminders.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        return new PagedResponse<PaymentReminderResponse>([.. items.Select(x => x.Reminder.ToResponse(x.CustomerName))], page, size, total);
    }
}

internal sealed class GetReminderSettingsHandler(PaymentReminderService service) : IQueryHandler<GetReminderSettingsQuery, Result<ReminderSettingsResponse>>
{
    public async Task<Result<ReminderSettingsResponse>> HandleAsync(GetReminderSettingsQuery query, CancellationToken cancellationToken) =>
        service.Describe(await service.SettingsAsync(cancellationToken).ConfigureAwait(false));
}

internal sealed class UpdateReminderSettingsHandler(
    IReminderSettingsRepository settings, PaymentReminderService service, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UpdateReminderSettingsCommand, Result<ReminderSettingsResponse>>
{
    public async Task<Result<ReminderSettingsResponse>> HandleAsync(UpdateReminderSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReminderSettings? current = await settings.FindAsync(cancellationToken).ConfigureAwait(false);
        bool created = current is null;
        current ??= ReminderSettings.Default();
        UpdateReminderSettingsRequest request = command.Request ?? new UpdateReminderSettingsRequest();
        Result updated = current.Update(request.AutoSend, request.Delays ?? [], request.MinimumGapDays, user.UserId, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return Result<ReminderSettingsResponse>.Failure(updated.Errors);
        }

        if (created)
        {
            await settings.AddAsync(current, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return service.Describe(current);
    }
}
