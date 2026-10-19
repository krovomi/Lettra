using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Accounting;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Consultation de la comptabilité du dossier (module Exports) : journal des ventes, balance et grand livre d'un compte,
/// lus dans les écritures enregistrées à la validation des pièces, comme le FEC. Lecture seule.
/// </summary>
internal static class AccountingEndpoints
{
    public static RouteGroupBuilder MapAccounting(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/accounting").WithTags("Accounting");

        group.MapGet("/journal", GetJournalAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetSalesJournal")
            .Produces<SalesJournalResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/balance", GetBalanceAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetTrialBalance")
            .Produces<TrialBalanceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/accounts/{account}/ledger", GetLedgerAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetAccountLedger")
            .Produces<AccountLedgerResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> GetJournalAsync(
        DateOnly from,
        DateOnly to,
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string? search = null,
        string? account = null,
        Guid? customerId = null,
        int page = 1,
        int pageSize = 50)
    {
        Result<SalesJournalResponse> result = await queries
            .QueryAsync(new GetSalesJournalQuery(
                tenant.Current.DossierId, from, to, ListSearch.Normalize(search),
                ListSearch.Normalize(account), customerId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> GetBalanceAsync(DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<TrialBalanceResponse> result = await queries
            .QueryAsync(new GetTrialBalanceQuery(tenant.Current.DossierId, from, to), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> GetLedgerAsync(
        string account, DateOnly from, DateOnly to, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken, Guid? customerId = null)
    {
        Result<AccountLedgerResponse> result = await queries
            .QueryAsync(new GetAccountLedgerQuery(tenant.Current.DossierId, account, from, to, customerId), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }
}
