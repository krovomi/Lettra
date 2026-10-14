using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Parties;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Écriture des fournisseurs : on manipule l'agrégat.</summary>
public interface ISupplierRepository
{
    Task AddAsync(Supplier supplier, CancellationToken cancellationToken);
}

/// <summary>Lecture des fournisseurs : projection directe en contrats.</summary>
public interface ISupplierReadStore
{
    Task<PagedResponse<SupplierSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<SupplierResponse?> GetAsync(SupplierId id, CancellationToken cancellationToken);
}
