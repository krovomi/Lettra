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
}

/// <summary>Écran Catalogue, et liste de choix F4 des futurs écrans Devis et Factures.</summary>
public interface ICatalogGateway
{
    Task<OperationResult<CatalogReferenceData>> GetReferenceDataAsync(CancellationToken cancellationToken);

    Task<OperationResult<PagedResponse<CatalogItemSummary>>> ListAsync(string? search, bool includeArchived, int page, int pageSize, CancellationToken cancellationToken);

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
