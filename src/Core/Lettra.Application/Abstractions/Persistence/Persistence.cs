using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Valide les changements de la base du dossier courant en une transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Écriture des clients (côté C de CQRS) : on manipule l'agrégat.</summary>
public interface ICustomerRepository
{
    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task<Customer?> FindAsync(CustomerId id, CancellationToken cancellationToken);
}

/// <summary>Lecture des clients (côté Q de CQRS) : projection directe en contrats, sans passer par l'agrégat.</summary>
public interface ICustomerReadStore
{
    /// <summary>Clients dont le nom contient <paramref name="search"/>, sans tenir compte des accents ni de la casse.</summary>
    Task<PagedResponse<CustomerSummary>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken);
}
