using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;

namespace Lettra.UI.Abstractions;

/// <summary>Écran Fournisseurs. Deux implémentations (in-process, HTTP), même résultat, mêmes codes d'erreur.</summary>
public interface ISuppliersGateway
{
    Task<OperationResult<SupplierResponse>> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<SupplierSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Liste filtrée, triée et paginée de la page de consultation (voir <see cref="PartyListQuery"/>).</summary>
    Task<OperationResult<PagedResponse<SupplierSummary>>> SearchAsync(PartyListQuery query, CancellationToken cancellationToken);

    Task<OperationResult<SupplierResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Modifie un fournisseur à partir de la version lue (ADR-0013), comme <see cref="ICatalogGateway.UpdateAsync"/>.</summary>
    Task<OperationResult<SupplierResponse>> UpdateAsync(Guid id, long expectedVersion, CreateSupplierRequest request, CancellationToken cancellationToken);

    /// <summary>Supprime un fournisseur (retiré des listes, gardé en base), comme <see cref="ICustomersGateway.DeleteAsync"/>.</summary>
    Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);
}

/// <summary>
/// Critères de la liste des articles : référence ou désignation contenant <paramref name="Search"/>, archivés compris sur
/// demande, nature, tri (par défaut la référence, de A à Z).
/// </summary>
public sealed record CatalogListQuery(
    string? Search = null,
    bool IncludeArchived = false,
    int Page = 1,
    int PageSize = 50,
    CatalogSortDto Sort = CatalogSortDto.Reference,
    SortDirectionDto Direction = SortDirectionDto.Ascending,
    CatalogItemKindDto? Kind = null);

/// <summary>Écran Catalogue, et liste de choix F4 des futurs écrans Devis et Factures.</summary>
public interface ICatalogGateway
{
    Task<OperationResult<CatalogReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<CatalogItemSummary>>> ListAsync(CatalogListQuery query, CancellationToken cancellationToken);

    Task<OperationResult<CatalogItemResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<CatalogItemResponse>> CreateAsync(SaveCatalogItemRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Modifie un article à partir de la version lue (ADR-0013). Si quelqu'un l'a modifié entre-temps : échec
    /// <c>Concurrency.VersionMismatch</c> avec l'article actuel dans <see cref="OperationResult{T}.Current"/>.
    /// </summary>
    Task<OperationResult<CatalogItemResponse>> UpdateAsync(Guid id, long expectedVersion, SaveCatalogItemRequest request, CancellationToken cancellationToken);

    Task<OperationResult<CatalogItemResponse>> SetArchivedAsync(Guid id, long expectedVersion, bool isArchived, CancellationToken cancellationToken);
}

/// <summary>
/// Annuaires publics (SIRENE, VIES), appelés seulement à la demande de l'utilisateur. Hors ligne, les
/// appels renvoient <c>Directory.Unavailable</c> ou <c>Api.Unreachable</c> : la saisie manuelle reste possible.
/// </summary>
public interface IDirectoryGateway
{
    Task<OperationResult<CompanySearchResponse>> SearchCompaniesAsync(string query, int maxResults, CancellationToken cancellationToken);

    Task<OperationResult<VatNumberCheckResponse>> CheckVatNumberAsync(string vatNumber, CancellationToken cancellationToken);
}
