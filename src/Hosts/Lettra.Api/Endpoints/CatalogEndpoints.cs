using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>Routes du module Catalogue. Les écritures renvoient l'article relu, comme le fait la gateway in-process.</summary>
internal static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalog(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/catalog").WithTags("Catalog");

        group.MapGet("/reference-data", ReferenceDataAsync)
            .RequirePermission(Permission.CatalogueConsulter)
            .WithName("GetCatalogReferenceData")
            .Produces<CatalogReferenceData>();

        group.MapGet("/items", ListAsync)
            .RequirePermission(Permission.CatalogueConsulter)
            .WithName("ListCatalogItems")
            .Produces<PagedResponse<CatalogItemSummary>>();

        group.MapGet("/items/{id:guid}", GetAsync)
            .RequirePermission(Permission.CatalogueConsulter)
            .WithName("GetCatalogItem")
            .Produces<CatalogItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/items", CreateAsync)
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("CreateCatalogItem")
            .Produces<CatalogItemResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/items/{id:guid}", UpdateAsync)
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("UpdateCatalogItem")
            .Produces<CatalogItemResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/items/{id:guid}/archived", SetArchivedAsync)
            .RequirePermission(Permission.CatalogueGerer)
            .WithName("SetCatalogItemArchived")
            .Produces<CatalogItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ReferenceDataAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<CatalogReferenceData> result = await queries
            .QueryAsync(new GetCatalogReferenceDataQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> ListAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string? search = null,
        bool includeArchived = false,
        int page = 1,
        int pageSize = 50)
    {
        Result<PagedResponse<CatalogItemSummary>> result = await queries
            .QueryAsync(new ListCatalogItemsQuery(tenant.Current.DossierId, search, includeArchived, page, pageSize), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> GetAsync(Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<CatalogItemResponse> result = await queries
            .QueryAsync(new GetCatalogItemQuery(tenant.Current.DossierId, new CatalogItemId(id)), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> CreateAsync(
        SaveCatalogItemRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<CatalogItemId> created = await commands.SendAsync(request.ToCreateCommand(dossier), cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Errors.ToProblem();
        }

        Result<CatalogItemResponse> read = await queries.QueryAsync(new GetCatalogItemQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false);
        return read.IsSuccess
            ? TypedResults.Created($"/api/v1/catalog/items/{read.Value.Id}", read.Value)
            : read.Errors.ToProblem();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        SaveCatalogItemRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        CatalogItemId itemId = new(id);
        Result updated = await commands.SendAsync(request.ToUpdateCommand(dossier, itemId), cancellationToken).ConfigureAwait(false);
        return updated.IsFailure ? updated.Errors.ToProblem() : await ReadAsync(queries, dossier, itemId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> SetArchivedAsync(
        Guid id,
        SetCatalogItemArchivedRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        CatalogItemId itemId = new(id);
        Result changed = await commands.SendAsync(new SetCatalogItemArchivedCommand(dossier, itemId, request.IsArchived), cancellationToken).ConfigureAwait(false);
        return changed.IsFailure ? changed.Errors.ToProblem() : await ReadAsync(queries, dossier, itemId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> ReadAsync(IQueryDispatcher queries, DossierId dossier, CatalogItemId id, CancellationToken cancellationToken)
    {
        Result<CatalogItemResponse> read = await queries.QueryAsync(new GetCatalogItemQuery(dossier, id), cancellationToken).ConfigureAwait(false);
        return read.IsSuccess ? TypedResults.Ok(read.Value) : read.Errors.ToProblem();
    }
}
