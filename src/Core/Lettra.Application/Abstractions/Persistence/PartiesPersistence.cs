using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Écriture des fournisseurs : on manipule l'agrégat.</summary>
public interface ISupplierRepository
{
    Task AddAsync(Supplier supplier, CancellationToken cancellationToken);

    Task<Supplier?> FindAsync(SupplierId id, CancellationToken cancellationToken);
}

/// <summary>
/// Critères d'une page de clients ou de fournisseurs : nom contenant <see cref="Search"/> (sans accents ni casse), tri,
/// et pour les clients seulement, leur nature (<see cref="Kind"/>).
/// </summary>
public sealed record PartyListFilter(
    string? Search,
    int Page,
    int PageSize,
    PartySortDto Sort = PartySortDto.Name,
    SortDirectionDto Direction = SortDirectionDto.Ascending,
    CustomerKind? Kind = null);

/// <summary>Lecture des fournisseurs : projection directe en contrats.</summary>
public interface ISupplierReadStore
{
    Task<PagedResponse<SupplierSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken);

    Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken);
}
