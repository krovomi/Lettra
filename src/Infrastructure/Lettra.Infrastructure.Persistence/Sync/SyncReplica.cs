using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Mapping;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>Outbox du dossier ouvert sur le poste.</summary>
internal sealed class EfSyncOutbox(TenantDbSession session) : ISyncOutbox
{
    public async Task<IReadOnlyList<SyncOutboxItem>> ListAsync(SyncOutboxStatus? status, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<SyncOutboxRecord> query = db.Set<SyncOutboxRecord>().AsNoTracking();
        if (status is SyncOutboxStatus wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        List<SyncOutboxRecord> rows = await query.OrderBy(r => r.Sequence).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => new SyncOutboxItem(r.Sequence, r.Intent, r.Status, r.Detail))];
    }

    public async Task AcknowledgeAsync(IReadOnlyList<SyncIntentResult> results, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return;
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Guid> keys = [.. results.Select(r => r.IdempotencyKey)];
        Dictionary<Guid, SyncOutboxRecord> rows = await db.Set<SyncOutboxRecord>().AsTracking()
            .Where(r => keys.Contains(r.IdempotencyKey))
            .ToDictionaryAsync(r => r.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        foreach (SyncIntentResult result in results)
        {
            if (!rows.TryGetValue(result.IdempotencyKey, out SyncOutboxRecord? row) || row.Status != SyncOutboxStatus.Pending)
            {
                continue;
            }

            SyncOutboxStatus status = result.Outcome switch
            {
                SyncIntentOutcome.Rejected => SyncOutboxStatus.Rejected,
                SyncIntentOutcome.Conflict => SyncOutboxStatus.Conflict,
                _ => SyncOutboxStatus.Sent,
            };
            string? detail = result.Errors.Count == 0 ? null : string.Join(' ', result.Errors.Select(e => e.Code));
            row.Acknowledge(status, detail is { Length: > 1000 } ? detail[..1000] : detail, now);
            foreach (SyncFieldConflict conflict in result.Conflicts)
            {
                db.Set<SyncConflictRow>().Add(new SyncConflictRow(result, row.Entity, conflict, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<SyncConflictRecord>> ListConflictsAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<SyncConflictRow> rows = await db.Set<SyncConflictRow>().AsNoTracking().OrderBy(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
    }
}

/// <summary>
/// Réplique SQLite du poste : reprend l'état du serveur élément par élément, avec sa version, sans passer par les
/// commandes. Un élément dont une intention n'est pas encore partie garde la saisie du poste ; il sera repris à la
/// réception suivante, une fois l'intention acquittée.
/// </summary>
internal sealed class EfSyncReplica(TenantDbSession session, TimeProvider clock) : ISyncReplica
{
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SyncStateRecord>().AsNoTracking()
            .Where(r => r.Key == SyncStateRecord.TokenKey).Select(r => r.Value)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncApplyReport> ApplyAsync(SyncChangesResponse changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        int applied = 0, kept = 0, failed = 0;

        IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            HashSet<(SyncEntityKind, Guid)> pending = [.. (await db.Set<SyncOutboxRecord>().AsNoTracking()
                .Where(r => r.Status == SyncOutboxStatus.Pending)
                .Select(r => new { r.Entity, r.EntityId })
                .ToListAsync(cancellationToken).ConfigureAwait(false)).Select(r => (r.Entity, r.EntityId))];

            async Task Apply(SyncEntityKind kind, Guid id, Func<Task<bool?>> write)
            {
                if (pending.Contains((kind, id)))
                {
                    kept++;
                    return;
                }

                try
                {
                    bool? changed = await write().ConfigureAwait(false);
                    if (changed is null)
                    {
                        failed++;
                    }
                    else if (changed.Value)
                    {
                        applied++;
                    }
                }
                catch (DbUpdateException)
                {
                    // Réplique SQLite : une contrainte refusée n'annule pas la transaction ; l'élément suivant continue.
                    failed++;
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }

            foreach (CustomerResponse c in changes.Customers)
            {
                await Apply(SyncEntityKind.Customer, c.Id, () => UpsertCustomerAsync(db, c, cancellationToken)).ConfigureAwait(false);
            }

            foreach (SupplierResponse s in changes.Suppliers)
            {
                await Apply(SyncEntityKind.Supplier, s.Id, () => UpsertSupplierAsync(db, s, cancellationToken)).ConfigureAwait(false);
            }

            foreach (CatalogItemResponse i in changes.CatalogItems)
            {
                await Apply(SyncEntityKind.CatalogItem, i.Id, () => UpsertCatalogItemAsync(db, i, cancellationToken)).ConfigureAwait(false);
            }

            SyncStateRecord? token = await db.Set<SyncStateRecord>().AsTracking()
                .FirstOrDefaultAsync(r => r.Key == SyncStateRecord.TokenKey, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                db.Set<SyncStateRecord>().Add(new SyncStateRecord(SyncStateRecord.TokenKey, changes.Token));
            }
            else
            {
                token.Set(changes.Token);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SyncApplyReport(applied, kept, failed);
    }

    /// <returns><c>true</c> écrit, <c>false</c> déjà à jour, <c>null</c> refusé par le domaine.</returns>
    private async Task<bool?> UpsertCustomerAsync(LettraDbContext db, CustomerResponse server, CancellationToken ct)
    {
        CustomerId id = new(server.Id);
        Customer? local = await db.Customers.AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (local is null)
        {
            Result<Customer> created = Customer.Create(id, server.ToRequest().ToDraft(), server.CreatedAt);
            if (created.IsFailure)
            {
                return null;
            }

            db.Customers.Add(created.Value);
        }
        else
        {
            if (local.Version == server.Version && local.ToResponse().ToRequest() == server.ToRequest())
            {
                return false;
            }

            if (local.Update(server.ToRequest().ToDraft(), clock.GetUtcNow()).IsFailure)
            {
                return null;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.Customers.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Version, server.Version), ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool?> UpsertSupplierAsync(LettraDbContext db, SupplierResponse server, CancellationToken ct)
    {
        SupplierId id = new(server.Id);
        Supplier? local = await db.Set<Supplier>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (local is null)
        {
            Result<Supplier> created = Supplier.Create(id, server.ToRequest().ToDraft(), server.CreatedAt);
            if (created.IsFailure)
            {
                return null;
            }

            db.Set<Supplier>().Add(created.Value);
        }
        else
        {
            if (local.Version == server.Version && local.ToResponse().ToRequest() == server.ToRequest())
            {
                return false;
            }

            if (local.Update(server.ToRequest().ToDraft(), clock.GetUtcNow()).IsFailure)
            {
                return null;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.Set<Supplier>().Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Version, server.Version), ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool?> UpsertCatalogItemAsync(LettraDbContext db, CatalogItemResponse server, CancellationToken ct)
    {
        CatalogItemId id = new(server.Id);
        DateTimeOffset now = clock.GetUtcNow();
        CatalogItem? local = await db.Set<CatalogItem>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (local is null)
        {
            Result<CatalogItem> created = CatalogItem.Create(id, server.ToRequest().ToDraft(), server.CreatedAt);
            if (created.IsFailure)
            {
                return null;
            }

            created.Value.SetArchived(server.IsArchived, server.UpdatedAt);
            db.Set<CatalogItem>().Add(created.Value);
        }
        else
        {
            CatalogItemResponse current = local.ToResponse();
            if (local.Version == server.Version && current.ToRequest() == server.ToRequest() && current.IsArchived == server.IsArchived)
            {
                return false;
            }

            // Un article archivé n'est plus modifiable : on le réactive le temps de reprendre ses données.
            local.SetArchived(false, now);
            if (local.Update(server.ToRequest().ToDraft(), now).IsFailure)
            {
                return null;
            }

            local.SetArchived(server.IsArchived, now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await db.Set<CatalogItem>().Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Version, server.Version).SetProperty(e => e.UpdatedAt, server.UpdatedAt), ct)
            .ConfigureAwait(false);
        return true;
    }
}
