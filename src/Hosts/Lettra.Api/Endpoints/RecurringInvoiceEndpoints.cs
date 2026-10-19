using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Routes des factures récurrentes (vague 7) : modèles (version en <c>ETag</c>, <c>If-Match</c> obligatoire pour les
/// écritures, ADR-0013) et création des factures dues, à la demande ; le Worker fait la même chose à intervalle régulier.
/// </summary>
internal static class RecurringInvoiceEndpoints
{
    public static RouteGroupBuilder MapRecurringInvoices(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/recurring-invoices").WithTags("RecurringInvoices");
        group.MapGet("/", ListAsync).RequirePermission(Permission.DossierLire).WithName("ListRecurringInvoices")
            .Produces<PagedResponse<RecurringInvoiceSummary>>();
        group.MapGet("/{id:guid}", GetAsync).RequirePermission(Permission.DossierLire).WithName("GetRecurringInvoice")
            .Produces<RecurringInvoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateAsync).RequirePermission(Permission.FactureSaisir).WithName("CreateRecurringInvoice")
            .Produces<RecurringInvoiceResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPut("/{id:guid}", UpdateAsync).RequirePermission(Permission.FactureSaisir).WithName("UpdateRecurringInvoice")
            .Produces<RecurringInvoiceResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
            .RequireIfMatch();
        group.MapPut("/{id:guid}/active", SetActiveAsync).RequirePermission(Permission.FactureSaisir).WithName("SetRecurringInvoiceActive")
            .Produces<RecurringInvoiceResponse>().RequireIfMatch();
        group.MapDelete("/{id:guid}", DeleteAsync).RequirePermission(Permission.FactureSaisir).WithName("DeleteRecurringInvoice")
            .Produces(StatusCodes.Status204NoContent).RequireIfMatch();
        group.MapPost("/run", RunAsync).RequirePermission(Permission.FactureSaisir).WithName("RunRecurringInvoices")
            .Produces<RecurringRunResponse>();
        group.MapPost("/{id:guid}/run", RunOneAsync).RequirePermission(Permission.FactureSaisir).WithName("RunRecurringInvoice")
            .Produces<RecurringRunResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        return api;
    }

    private static async Task<IResult> ListAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string? search = null,
        Guid? customerId = null,
        bool? active = null,
        bool dueOnly = false,
        RecurringInvoiceSortDto sort = RecurringInvoiceSortDto.NextIssueDate,
        bool descending = false,
        int page = 1,
        int pageSize = 50)
    {
        Result<PagedResponse<RecurringInvoiceSummary>> result = await queries.QueryAsync(
            new ListRecurringInvoicesQuery(tenant.Current.DossierId, search, customerId, active, dueOnly, sort, descending, page, pageSize), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static Task<IResult> GetAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        ReadAsync(queries, tenant.Current.DossierId, new RecurringInvoiceId(id), cancellationToken);

    private static async Task<IResult> CreateAsync(
        SaveRecurringInvoiceRequest request, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<RecurringInvoiceId> created = await commands.SendAsync(new CreateRecurringInvoiceCommand(dossier, request), cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Errors.ToProblem();
        }

        Result<RecurringInvoiceResponse> read = await queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/recurring-invoices/{created.Value.Value}", read, r => r.Version);
    }

    private static Task<IResult> UpdateAsync(
        Guid id, SaveRecurringInvoiceRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        WriteAsync(new UpdateRecurringInvoiceCommand(tenant.Current.DossierId, new RecurringInvoiceId(id), request, http.IfMatchVersion()), commands, queries, cancellationToken);

    private static Task<IResult> SetActiveAsync(
        Guid id, SetRecurringInvoiceActiveRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        WriteAsync(
            new SetRecurringInvoiceActiveCommand(tenant.Current.DossierId, new RecurringInvoiceId(id), request.IsActive, http.IfMatchVersion()), commands, queries, cancellationToken);

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        RecurringInvoiceId templateId = new(id);
        Result deleted = await commands.SendAsync(new DeleteRecurringInvoiceCommand(dossier, templateId, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? TypedResults.NoContent()
            : await ConcurrencyResults.WriteFailureAsync(
                deleted.Errors, () => queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, templateId), cancellationToken), r => r.Version).ConfigureAwait(false);
    }

    private static async Task<IResult> RunAsync(ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new GenerateRecurringInvoicesCommand(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RunOneAsync(Guid id, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new GenerateRecurringInvoicesCommand(tenant.Current.DossierId, new RecurringInvoiceId(id)), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> WriteAsync<TCommand>(
        TCommand command, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
        where TCommand : ICommand<Result>, IDossierScoped, ITargetsRecurringInvoice
    {
        Result done = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        Task<Result<RecurringInvoiceResponse>> Read() => queries.QueryAsync(new GetRecurringInvoiceQuery(command.Dossier, command.RecurringInvoiceId), cancellationToken);
        return done.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(done.Errors, Read, r => r.Version).ConfigureAwait(false)
            : await ReadAsync(queries, command.Dossier, command.RecurringInvoiceId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> ReadAsync(IQueryDispatcher queries, DossierId dossier, RecurringInvoiceId id, CancellationToken cancellationToken)
    {
        Result<RecurringInvoiceResponse> result = await queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, id), cancellationToken).ConfigureAwait(false);
        return Versioned(result, r => r.Version);
    }
}
