using Lettra.Contracts.Accounting;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Tests.Consultation;

/// <summary>Comptabilité fictive : les tests posent les écritures, la passerelle filtre comme le serveur.</summary>
internal sealed class FakeAccountingGateway : IAccountingGateway
{
    public List<JournalEntryResponse> Entries { get; } = [];

    public List<JournalQuery> JournalQueries { get; } = [];

    public List<(string Account, DateOnly From, DateOnly To, Guid? CustomerId)> LedgerQueries { get; } = [];

    public string? NextError { get; set; }

    public static JournalEntryResponse Invoice(string piece, DateOnly date, Guid customerId, string customerName, decimal net, decimal vat, string revenueAccount = "706") =>
        new(Guid.CreateVersion7(), InvoiceKindDto.Invoice, piece, date, customerId, customerName, net + vat,
        [
            new JournalLineResponse("411", net + vat, 0m, null),
            new JournalLineResponse(revenueAccount, 0m, net, 20m),
            new JournalLineResponse("44571", 0m, vat, 20m),
        ]);

    public Task<OperationResult<SalesJournalResponse>> GetJournalAsync(JournalQuery query, CancellationToken cancellationToken)
    {
        JournalQueries.Add(query);
        if (Error<SalesJournalResponse>() is { } failure)
        {
            return Task.FromResult(failure);
        }

        List<JournalEntryResponse> kept = InPeriod(query.From, query.To)
            .Where(e => query.CustomerId is null || e.CustomerId == query.CustomerId)
            .Where(e => query.Account is null || e.Lines.Any(l => l.Account == query.Account))
            .Where(e => string.IsNullOrWhiteSpace(query.Search)
                || e.PieceNumber.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || e.CustomerName.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Date)
            .ToList();
        List<JournalEntryResponse> page = kept.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        decimal debit = kept.SelectMany(e => e.Lines).Sum(l => l.Debit);
        decimal credit = kept.SelectMany(e => e.Lines).Sum(l => l.Credit);
        return Task.FromResult(OperationResult<SalesJournalResponse>.Success(
            new SalesJournalResponse(query.From, query.To, page, query.Page, query.PageSize, kept.Count, debit, credit)));
    }

    public Task<OperationResult<TrialBalanceResponse>> GetBalanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (Error<TrialBalanceResponse>() is { } failure)
        {
            return Task.FromResult(failure);
        }

        List<JournalEntryResponse> kept = InPeriod(from, to).ToList();
        List<AccountBalanceResponse> accounts = kept
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.Account)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new AccountBalanceResponse(g.Key, g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Count()))
            .ToList();
        List<CustomerBalanceResponse> customers = kept
            .GroupBy(e => (e.CustomerId, e.CustomerName))
            .Select(g => new CustomerBalanceResponse(g.Key.CustomerId, g.Key.CustomerName,
                g.SelectMany(e => e.Lines).Where(l => l.Account == "411").Sum(l => l.Debit),
                g.SelectMany(e => e.Lines).Where(l => l.Account == "411").Sum(l => l.Credit),
                g.Count()))
            .OrderByDescending(c => c.Balance)
            .ToList();
        return Task.FromResult(OperationResult<TrialBalanceResponse>.Success(new TrialBalanceResponse(from, to, accounts, customers,
            accounts.Sum(a => a.Debit), accounts.Sum(a => a.Credit))));
    }

    public Task<OperationResult<AccountLedgerResponse>> GetLedgerAsync(string account, DateOnly from, DateOnly to, Guid? customerId, CancellationToken cancellationToken)
    {
        LedgerQueries.Add((account, from, to, customerId));
        if (Error<AccountLedgerResponse>() is { } failure)
        {
            return Task.FromResult(failure);
        }

        IEnumerable<(JournalEntryResponse Entry, JournalLineResponse Line)> Moves(IEnumerable<JournalEntryResponse> entries) => entries
            .Where(e => customerId is null || e.CustomerId == customerId)
            .SelectMany(e => e.Lines.Where(l => l.Account == account).Select(l => (e, l)));

        decimal opening = Moves(Entries.Where(e => e.Date < from)).Sum(m => m.Line.Debit - m.Line.Credit);
        decimal running = opening;
        List<LedgerLineResponse> lines = [];
        foreach ((JournalEntryResponse entry, JournalLineResponse line) in Moves(InPeriod(from, to)).OrderBy(m => m.Entry.Date))
        {
            running += line.Debit - line.Credit;
            lines.Add(new LedgerLineResponse(entry.Date, entry.InvoiceId, entry.Kind, entry.PieceNumber, entry.CustomerId, entry.CustomerName, line.Debit, line.Credit, running));
        }

        decimal debit = lines.Sum(l => l.Debit);
        decimal credit = lines.Sum(l => l.Credit);
        return Task.FromResult(OperationResult<AccountLedgerResponse>.Success(
            new AccountLedgerResponse(account, customerId, from, to, opening, lines, debit, credit, opening + debit - credit)));
    }

    private IEnumerable<JournalEntryResponse> InPeriod(DateOnly from, DateOnly to) => Entries.Where(e => e.Date >= from && e.Date <= to);

    private OperationResult<T>? Error<T>()
    {
        if (NextError is not { } code)
        {
            return null;
        }

        NextError = null;
        return OperationResult<T>.Failure(code);
    }
}
