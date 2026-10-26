using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Insights;

/// <summary>La part d'un encaissement imputée sur une facture.</summary>
public sealed record PaymentShare(Guid InvoiceId, decimal Amount);

/// <summary>
/// Ce que le journal ne porte pas et que le dossier sait aujourd'hui : le nom des clients, l'imputation de chaque
/// encaissement sur les factures, et ce qui reste dû aujourd'hui sur chaque facture validée.
/// </summary>
public sealed record DossierPresent(
    IReadOnlyDictionary<Guid, string> CustomerNames,
    IReadOnlyDictionary<Guid, IReadOnlyList<PaymentShare>> AllocationsByPayment,
    IReadOnlyDictionary<Guid, decimal> OutstandingNowByInvoice)
{
    public static DossierPresent Empty { get; } = new(new Dictionary<Guid, string>(), new Dictionary<Guid, IReadOnlyList<PaymentShare>>(), new Dictionary<Guid, decimal>());
}

/// <summary>Lecture, dans la base du dossier, de ce que le rejeu du journal ne sait pas (lecture seule).</summary>
public interface IDossierPresentReader
{
    Task<DossierPresent> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Le dossier tel qu'il était à l'instant <paramref name="At"/>, rejoué depuis le journal inaltérable.</summary>
public sealed record GetDossierAsOfQuery(DossierId Dossier, DateTimeOffset At) : IQuery<Result<DossierAsOfResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Le rejeu du journal, pur et déterministe : on lui donne les entrées jusqu'à l'instant, dans l'ordre, et il en tire
/// ce que le dossier montrait alors.
/// <list type="bullet">
/// <item>chiffre d'affaires de l'année du jour légal de l'instant : factures et acomptes validés moins les avoirs, à leur
/// date d'émission (la règle des clôtures) ;</item>
/// <item>ce qui restait dû sur chaque facture : son total, moins les avoirs validés sur elle et les encaissements
/// enregistrés et pas encore contre-passés à l'instant, selon leur imputation ;</item>
/// <item>le solde de chaque client : facturé, moins les avoirs et les encaissements reçus (un encaissement non imputé
/// réduit le solde, comme au compte 411).</item>
/// </list>
/// </summary>
public sealed class DossierReplay(DateTimeOffset at)
{
    private readonly DateOnly _legalDate = LegalCalendar.DateOf(at);
    private readonly Dictionary<Guid, ReplayedDocument> _documents = [];
    private readonly Dictionary<Guid, CustomerAccount> _customers = [];
    private readonly Dictionary<Guid, JournalEntry> _payments = [];
    private readonly decimal[,] _months = new decimal[12, 3];
    private readonly int[] _monthDocuments = new int[12];
    private readonly LinkedList<JournalEntry> _lastEvents = [];
    private int _quotes, _invoices, _creditNotes, _closings;
    private decimal _yearHt, _yearVat, _yearTtc;

    public long EntriesReplayed { get; private set; }

    /// <summary>L'entrée suivante, d'avant l'instant, dans l'ordre des numéros d'entrée.</summary>
    public void Add(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EntriesReplayed++;
        _lastEvents.AddFirst(entry);
        if (_lastEvents.Count > DossierAsOfResponse.LastEventCount)
        {
            _lastEvents.RemoveLast();
        }

        switch (entry.Kind)
        {
            case JournalEntryKind.QuoteValidated:
                _quotes++;
                break;
            case JournalEntryKind.InvoiceValidated when entry.DocumentId is Guid id:
                _invoices++;
                _documents[id] = new ReplayedDocument(entry);
                Account(entry.CustomerId).Invoiced += entry.TotalInclTax;
                CountRevenue(entry);
                break;
            case JournalEntryKind.CreditNoteValidated:
                _creditNotes++;
                if (entry.RelatedDocumentId is Guid invoice && _documents.TryGetValue(invoice, out ReplayedDocument? credited))
                {
                    credited.Credited += entry.TotalInclTax;
                }

                Account(entry.CustomerId).Credited += entry.TotalInclTax;
                CountRevenue(entry);
                break;
            case JournalEntryKind.PaymentRecorded when entry.DocumentId is Guid payment:
                _payments[payment] = entry;
                break;
            case JournalEntryKind.PaymentReversed when entry.DocumentId is Guid payment:
                _payments.Remove(payment);
                break;
            case JournalEntryKind.PeriodClosed:
                _closings++;
                break;
        }
    }

    /// <summary>Ce que le dossier montrait à l'instant, complété par ce qu'il sait aujourd'hui.</summary>
    public DossierAsOfResponse Complete(DossierPresent present, long entriesAfter, bool chainIntact, DateTimeOffset? firstEntryAt, PeriodClosing? lastClosing)
    {
        ArgumentNullException.ThrowIfNull(present);
        foreach ((Guid paymentId, JournalEntry payment) in _payments)
        {
            Account(payment.CustomerId).Paid += payment.TotalInclTax;
            foreach (PaymentShare share in present.AllocationsByPayment.GetValueOrDefault(paymentId) ?? [])
            {
                if (_documents.TryGetValue(share.InvoiceId, out ReplayedDocument? document))
                {
                    document.Paid += share.Amount;
                }
            }
        }

        string NameOf(Guid customer) => present.CustomerNames.GetValueOrDefault(customer) ?? string.Empty;
        decimal NowOf(Guid invoice) => present.OutstandingNowByInvoice.GetValueOrDefault(invoice);

        Dictionary<Guid, decimal> nowByCustomer = _documents.Values
            .GroupBy(d => d.CustomerId)
            .ToDictionary(g => g.Key, g => g.Sum(d => NowOf(d.Id)));
        DossierAsOfCustomerResponse[] customers =
        [
            .. _customers
                .Select(c => new DossierAsOfCustomerResponse(
                    c.Key, NameOf(c.Key), c.Value.Invoiced, c.Value.Credited, c.Value.Paid, c.Value.Balance, nowByCustomer.GetValueOrDefault(c.Key)))
                .Where(c => c.BalanceInclTax != 0m || c.BalanceNowInclTax != 0m)
                .OrderByDescending(c => c.BalanceInclTax)
                .ThenBy(c => c.CustomerName, StringComparer.CurrentCulture),
        ];
        DossierAsOfDocumentResponse[] open =
        [
            .. _documents.Values
                .Where(d => d.Outstanding > 0m)
                .OrderBy(d => d.IssueDate)
                .ThenBy(d => d.Number, StringComparer.Ordinal)
                .Select(d => new DossierAsOfDocumentResponse(
                    d.Id, d.Number, d.Kind, d.IssueDate, d.CustomerId, NameOf(d.CustomerId), d.Total, d.Outstanding, NowOf(d.Id))),
        ];

        return new DossierAsOfResponse(
            at,
            _legalDate,
            EntriesReplayed,
            entriesAfter,
            chainIntact,
            firstEntryAt,
            new DossierAsOfTotalsResponse(_yearHt, _yearVat, _yearTtc),
            [.. Enumerable.Range(0, _legalDate.Month).Select(m => new DossierAsOfMonthResponse(m + 1, _months[m, 0], _months[m, 1], _months[m, 2], _monthDocuments[m]))],
            new DossierAsOfCountsResponse(_quotes, _invoices, _creditNotes, _payments.Count, _closings),
            _customers.Values.Sum(c => c.Balance),
            present.OutstandingNowByInvoice.Values.Sum(),
            customers,
            open,
            lastClosing is null
                ? null
                : new DossierAsOfClosingResponse(PeriodClosing.FormatNumber(lastClosing.Number), lastClosing.PeriodEnd, lastClosing.ClosedAt, lastClosing.CumulativeInclTax),
            [.. _lastEvents.Select(e => new DossierAsOfEventResponse(e.Sequence, e.OccurredAt, (DossierAsOfEventKindDto)e.Kind, e.Number, AmountOf(e)))]);
    }

    /// <summary>Le montant qui parle de l'événement : celui d'une pièce, d'un encaissement ou le cumul d'une clôture.</summary>
    private static decimal? AmountOf(JournalEntry entry) => entry.Kind is JournalEntryKind.KeyRotated || entry.IsPlatformEvent ? null : entry.TotalInclTax;

    private void CountRevenue(JournalEntry entry)
    {
        if (entry.IssueDate is not { } issued || issued.Year != _legalDate.Year)
        {
            return;
        }

        (decimal ht, decimal vat, decimal ttc) = entry.RevenueContribution();
        _yearHt += ht;
        _yearVat += vat;
        _yearTtc += ttc;
        int month = issued.Month - 1;
        _months[month, 0] += ht;
        _months[month, 1] += vat;
        _months[month, 2] += ttc;
        _monthDocuments[month]++;
    }

    private CustomerAccount Account(Guid? customer)
    {
        Guid id = customer ?? Guid.Empty;
        if (!_customers.TryGetValue(id, out CustomerAccount? account))
        {
            _customers[id] = account = new CustomerAccount();
        }

        return account;
    }

    private sealed class CustomerAccount
    {
        public decimal Invoiced { get; set; }

        public decimal Credited { get; set; }

        public decimal Paid { get; set; }

        public decimal Balance => Invoiced - Credited - Paid;
    }

    private sealed class ReplayedDocument(JournalEntry entry)
    {
        public Guid Id { get; } = entry.DocumentId!.Value;

        public string Number { get; } = entry.Number ?? string.Empty;

        public DossierAsOfDocumentKindDto Kind { get; } = entry.DocumentKind == JournaledDocumentKind.DepositInvoice
            ? DossierAsOfDocumentKindDto.DownPaymentInvoice
            : DossierAsOfDocumentKindDto.Invoice;

        public DateOnly IssueDate { get; } = entry.IssueDate ?? LegalCalendar.DateOf(entry.OccurredAt);

        public Guid CustomerId { get; } = entry.CustomerId ?? Guid.Empty;

        public decimal Total { get; } = entry.TotalInclTax;

        public decimal Credited { get; set; }

        public decimal Paid { get; set; }

        public decimal Outstanding => Total - Credited - Paid;
    }
}

/// <summary>
/// Rejoue le journal jusqu'à l'instant demandé et vérifie, en passant, la chaîne de ces entrées (empreintes, chaînage,
/// sceaux, clôtures signées avant l'instant) : le dossier montré est celui que la chaîne garantit. Rien n'est écrit.
/// </summary>
internal sealed class GetDossierAsOfHandler(IIntegrityStore store, IIntegrityKeyring keyring, IDossierPresentReader present)
    : IQueryHandler<GetDossierAsOfQuery, Result<DossierAsOfResponse>>
{
    public async Task<Result<DossierAsOfResponse>> HandleAsync(GetDossierAsOfQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTimeOffset at = query.At;
        IReadOnlyList<PeriodClosing> closings = [.. (await store.ListClosingsAsync(cancellationToken).ConfigureAwait(false)).Where(c => c.ClosedAt <= at)];
        Result<IIntegrityKeyVerifier> keys = await keyring.VerifierAsync(cancellationToken).ConfigureAwait(false);
        IntegrityVerification? verification = keys.IsSuccess ? new IntegrityVerification(query.Dossier, closings, keys.Value) : null;

        DossierReplay replay = new(at);
        long after = 0;
        DateTimeOffset? first = null;
        await foreach (JournalEntry entry in store.ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            first ??= entry.OccurredAt;
            if (entry.OccurredAt > at)
            {
                after++;
                continue;
            }

            verification?.Check(entry);
            replay.Add(entry);
        }

        bool intact = replay.EntriesReplayed == 0 || verification is { Findings.Count: 0 };
        DossierPresent today = await present.ReadAsync(cancellationToken).ConfigureAwait(false);
        return replay.Complete(today, after, intact, first, closings.MaxBy(c => c.Number));
    }
}
