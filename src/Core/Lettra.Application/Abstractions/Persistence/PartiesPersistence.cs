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

/// <summary>Lecture des fournisseurs : projection directe en contrats.</summary>
public interface ISupplierReadStore
{
    /// <summary>Fournisseurs dont le nom contient <paramref name="search"/>, sans tenir compte des accents ni de la casse.</summary>
    Task<PagedResponse<SupplierSummary>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken);
}
