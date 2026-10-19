using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;
using Lettra.Exports.Application.Accounting;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes requêtes et même permission que les routes <c>/accounting</c> de l'API.</summary>
internal sealed class InProcessAccountingGateway(IServiceScopeFactory scopes) : IAccountingGateway
{
    public Task<OperationResult<SalesJournalResponse>> GetJournalAsync(JournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetSalesJournalQuery(
            dossier, query.From, query.To, ListSearch.Normalize(query.Search),
            ListSearch.Normalize(query.Account), query.CustomerId, query.Page, query.PageSize), ct), cancellationToken);
    }

    public Task<OperationResult<TrialBalanceResponse>> GetBalanceAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetTrialBalanceQuery(dossier, from, to), ct), cancellationToken);

    public Task<OperationResult<AccountLedgerResponse>> GetLedgerAsync(string account, DateOnly from, DateOnly to, Guid? customerId, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetAccountLedgerQuery(dossier, account, from, to, customerId), ct), cancellationToken);
}
