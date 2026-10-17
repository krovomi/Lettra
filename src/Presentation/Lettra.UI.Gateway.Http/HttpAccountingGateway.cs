using System.Globalization;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpAccountingGateway(HttpClient http) : IAccountingGateway
{
    public Task<OperationResult<SalesJournalResponse>> GetJournalAsync(JournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string account = string.IsNullOrWhiteSpace(query.Account) ? string.Empty : "&account=" + Uri.EscapeDataString(query.Account.Trim());
        string customer = query.CustomerId is Guid id ? $"&customerId={id}" : string.Empty;
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative(
                $"api/v1/accounting/journal?from={Day(query.From)}&to={Day(query.To)}&page={query.Page}&pageSize={query.PageSize}{HttpCalls.SearchParameter(query.Search)}{account}{customer}"), ct),
            ContractsJsonContext.Default.SalesJournalResponse,
            cancellationToken);
    }

    public Task<OperationResult<TrialBalanceResponse>> GetBalanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative($"api/v1/accounting/balance?from={Day(from)}&to={Day(to)}"), ct),
            ContractsJsonContext.Default.TrialBalanceResponse,
            cancellationToken);

    public Task<OperationResult<AccountLedgerResponse>> GetLedgerAsync(string account, DateOnly from, DateOnly to, Guid? customerId, CancellationToken cancellationToken)
    {
        string customer = customerId is Guid id ? $"&customerId={id}" : string.Empty;
        return HttpCalls.SendAsync(
            ct => http.GetAsync(HttpCalls.Relative(
                $"api/v1/accounting/accounts/{Uri.EscapeDataString(account ?? string.Empty)}/ledger?from={Day(from)}&to={Day(to)}{customer}"), ct),
            ContractsJsonContext.Default.AccountLedgerResponse,
            cancellationToken);
    }

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
