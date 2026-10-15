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
            ? TypedResults.Created($"/api/v1/suppliers/{read.Value.Id}", read.Value)
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
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> GetAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SupplierResponse> result = await queries
            .QueryAsync(new GetSupplierQuery(tenant.Current.DossierId, new SupplierId(id)), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
