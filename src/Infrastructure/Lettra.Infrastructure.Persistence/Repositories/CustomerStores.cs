using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Sync;
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
    public async Task<PagedResponse<CustomerSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Customer> query = db.Customers.AsNoTracking().Where(c => !c.IsArchived).WhereSearchKeyContains(filter.Search);
        if (filter.Kind is { } kind)
        {
            query = query.Where(c => c.Kind == kind);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        IOrderedQueryable<Customer> ordered = filter.Sort == PartySortDto.City
            ? query.By(c => c.BillingAddress.City, filter.Direction).ThenBy(c => c.Name)
            : query.By(c => c.Name, filter.Direction);

        // Les objets-valeurs convertis (Siren) sont projetés tels quels puis aplatis en mémoire.
        var rows = await ordered
            .ThenBy(c => c.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(c => new { c.Id, c.Kind, c.Name, c.Siren, c.BillingAddress.City })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<CustomerSummary> items = rows
            .Select(r => new CustomerSummary(r.Id.Value, (CustomerKindDto)(int)r.Kind, r.Name, r.Siren?.Value, r.City))
            .ToList();
        return new PagedResponse<CustomerSummary>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Customer? customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
        return customer?.ToResponse();
    }
}

internal sealed class UnitOfWork(TenantDbSession session, SyncOutboxStaging outbox) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);

        // Poste qui synchronise : l'intention de la commande part dans la même transaction que ses écritures (ADR-0007).
        foreach (SyncIntent intent in outbox.TakeAll())
        {
            db.Set<SyncOutboxRecord>().Add(new SyncOutboxRecord(intent));
        }

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

        // Les événements du domaine sont vidés après écriture : l'outbox du poste porte des intentions (contrats), pas des événements.
        foreach (IHasDomainEvents aggregate in db.ChangeTracker.Entries<IHasDomainEvents>().Select(e => e.Entity))
        {
            aggregate.ClearDomainEvents();
        }

        return written;
    }
}
