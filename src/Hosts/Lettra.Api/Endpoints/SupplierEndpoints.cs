using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Routes des fournisseurs (module Tiers).</summary>
internal static class SupplierEndpoints
{
    public static RouteGroupBuilder MapSuppliers(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/suppliers").WithTags("Suppliers");

        group.MapPost("/", CreateAsync)
            .RequirePermission(Permission.TiersCreer)
            .WithName("CreateSupplier")
            .Produces<SupplierResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.TiersConsulter)
            .WithName("ListSuppliers")
            .Produces<PagedResponse<SupplierSummary>>();

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permission.TiersConsulter)
            .WithName("GetSupplier")
            .Produces<SupplierResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("UpdateSupplier")
            .Produces<SupplierResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        // Suppression : retiré des listes, gardé en base pour ses pièces (204 ; 412 si la version lue est périmée).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("DeleteSupplier")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        return group;
    }

    private static async Task<IResult> CreateAsync(
        CreateSupplierRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<SupplierId> created = await commands.SendAsync(request.ToCommand(dossier), cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Errors.ToProblem();
        }

        Result<SupplierResponse> read = await queries.QueryAsync(new GetSupplierQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false);
        return read.IsSuccess
            ? TypedResults.Created($"/api/v1/suppliers/{read.Value.Id}", read.Value).WithETag(read.Value.Version)
            : read.Errors.ToProblem();
    }

    private static async Task<IResult> ListAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 50,
        string? search = null)
    {
        Result<PagedResponse<SupplierSummary>> result = await queries
            .QueryAsync(new ListSuppliersQuery(tenant.Current.DossierId, page, pageSize, string.IsNullOrWhiteSpace(search) ? null : search.Trim()), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> GetAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SupplierResponse> result = await queries
            .QueryAsync(new GetSupplierQuery(tenant.Current.DossierId, new SupplierId(id)), cancellationToken)
            .ConfigureAwait(false);
        return Versioned(result, v => v.Version);
    }

    /// <summary>Modification : même contrat qu'à la création, version lue en <c>If-Match</c> (ADR-0013).</summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        CreateSupplierRequest request,
        HttpContext http,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        SupplierId supplierId = new(id);
        Result updated = await commands
            .SendAsync(request.ToUpdateCommand(dossier, supplierId, http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        Task<Result<SupplierResponse>> Read() => queries.QueryAsync(new GetSupplierQuery(dossier, supplierId), cancellationToken);
        if (updated.IsFailure)
        {
            return await ConcurrencyResults.WriteFailureAsync(updated.Errors, Read, r => r.Version).ConfigureAwait(false);
        }

        Result<SupplierResponse> read = await Read().ConfigureAwait(false);
        return Versioned(read, v => v.Version);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext http,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        SupplierId supplierId = new(id);
        Result deleted = await commands.SendAsync(new DeleteSupplierCommand(dossier, supplierId, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? TypedResults.NoContent()
            : await ConcurrencyResults.WriteFailureAsync(deleted.Errors, () => queries.QueryAsync(new GetSupplierQuery(dossier, supplierId), cancellationToken), r => r.Version).ConfigureAwait(false);
    }
}
