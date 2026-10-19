using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Catalog;

/// <summary>
/// Lister les articles : écran Catalogue (archivés compris sur demande) et liste de choix F4 des devis et
/// factures (articles actifs, filtrés par référence ou désignation). Tri par référence, désignation ou prix ; filtre de
/// nature (bien ou service).
/// </summary>
public sealed record ListCatalogItemsQuery(
    DossierId Dossier,
    string? Search = null,
    bool IncludeArchived = false,
    int Page = 1,
    int PageSize = 50,
    CatalogSortDto Sort = CatalogSortDto.Reference,
    SortDirectionDto Direction = SortDirectionDto.Ascending,
    CatalogItemKindDto? Kind = null)
    : IQuery<Result<PagedResponse<CatalogItemSummary>>>, IRequirePermission
{
    public const int MaxPageSize = 500;
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.CatalogueConsulter;
}

/// <summary>Lire un article.</summary>
public sealed record GetCatalogItemQuery(DossierId Dossier, CatalogItemId CatalogItemId)
    : IQuery<Result<CatalogItemResponse>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueConsulter;
}

/// <summary>Listes fermées du catalogue (unités, taux de TVA) : le domaine en est la seule source.</summary>
public sealed record GetCatalogReferenceDataQuery(DossierId Dossier)
    : IQuery<Result<CatalogReferenceData>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueConsulter;
}

internal sealed class ListCatalogItemsValidator : IValidator<ListCatalogItemsQuery>
{
    public IReadOnlyList<Error> Validate(ListCatalogItemsQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListCatalogItemsQuery.MaxPageSize)];
        if (instance.Search is { Length: > ListCatalogItemsQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        if (instance.Kind is { } kind && !Enum.IsDefined(kind))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Kind)));
        }

        errors.AddRange(ListSortRules.Validate(instance.Sort, instance.Direction));
        return errors;
    }
}

internal sealed class ListCatalogItemsHandler(ICatalogItemReadStore store)
    : IQueryHandler<ListCatalogItemsQuery, Result<PagedResponse<CatalogItemSummary>>>
{
    public async Task<Result<PagedResponse<CatalogItemSummary>>> HandleAsync(ListCatalogItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string? search = ListSearch.Normalize(query.Search);
        CatalogItemFilter filter = new(search, query.IncludeArchived, query.Page, query.PageSize, query.Sort, query.Direction, (CatalogItemKind?)(int?)query.Kind);
        return await store.ListAsync(filter, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class GetCatalogItemHandler(ICatalogItemReadStore store)
    : IQueryHandler<GetCatalogItemQuery, Result<CatalogItemResponse>>
{
    public async Task<Result<CatalogItemResponse>> HandleAsync(GetCatalogItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CatalogItemResponse? item = await store.GetAsync(query.CatalogItemId, cancellationToken).ConfigureAwait(false);
        return item is null ? CatalogItemErrors.NotFound : item;
    }
}

internal sealed class GetCatalogReferenceDataHandler
    : IQueryHandler<GetCatalogReferenceDataQuery, Result<CatalogReferenceData>>
{
    private static readonly CatalogReferenceData Data = new(
        [.. UnitOfMeasure.All.Select(u => u.Code)],
        [.. VatRate.All.OrderByDescending(r => r.Percent).Select(r => new VatRateOption(r.Code, r.Percent))]);

    public Task<Result<CatalogReferenceData>> HandleAsync(GetCatalogReferenceDataQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CatalogReferenceData>>(Data);
}
