using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Repositories;

internal sealed class CustomerRepository(TenantDbSession session) : ICustomerRepository
{
    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Customers.AddAsync(customer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Customer?> FindAsync(CustomerId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Customers.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class CustomerReadStore(TenantDbSession session) : ICustomerReadStore
{
    public async Task<PagedResponse<CustomerSummary>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Customer> query = db.Customers.AsNoTracking().WhereSearchKeyContains(search);
        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Les objets-valeurs convertis (Siren) sont projetés tels quels puis aplatis en mémoire.
        var rows = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.Kind, c.Name, c.Siren, c.BillingAddress.City })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<CustomerSummary> items = rows
            .Select(r => new CustomerSummary(r.Id.Value, (CustomerKindDto)(int)r.Kind, r.Name, r.Siren?.Value, r.City))
            .ToList();
        return new PagedResponse<CustomerSummary>(items, page, pageSize, total);
    }

    public async Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Customer? customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        return customer?.ToResponse();
    }
}

internal sealed class UnitOfWork(TenantDbSession session) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        int written;
        try
        {
            written = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // La ligne a changé depuis sa lecture (ADR-0013) : le pipeline en fait un échec 412.
            throw new ConcurrencyConflictException("Écriture concurrente détectée à l'enregistrement.", ex);
        }

        // Phase 0 : pas encore d'outbox, les événements sont simplement vidés après écriture.
        // L'outbox (faits et intentions, ADR-0007) arrive avec la synchronisation.
        foreach (IHasDomainEvents aggregate in db.ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity))
        {
            aggregate.ClearDomainEvents();
        }

        return written;
    }
}
