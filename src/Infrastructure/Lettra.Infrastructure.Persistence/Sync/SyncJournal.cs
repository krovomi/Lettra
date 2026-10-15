using System.Globalization;
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
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>Intercepteur ajouté à chaque contexte de dossier par la fabrique, quand la composition root l'enregistre.</summary>
internal interface IDossierInterceptor : IInterceptor;

/// <summary>
/// Côté serveur : chaque enregistrement d'un client, d'un fournisseur ou d'un article ajoute une ligne au journal de
/// synchronisation, dans la même transaction. Rien n'y échappe (API, synchro, IA, Worker) puisque tout passe par EF.
/// </summary>
internal sealed class SyncJournalInterceptor(TimeProvider clock) : SaveChangesInterceptor, IDossierInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        List<SyncJournalRecord> lines = [];
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            (SyncEntityKind Kind, Guid Id)? synced = entry.Entity switch
            {
                Customer c => (SyncEntityKind.Customer, c.Id.Value),
                Supplier s => (SyncEntityKind.Supplier, s.Id.Value),
                CatalogItem i => (SyncEntityKind.CatalogItem, i.Id.Value),
                _ => null,
            };
            if (synced is { } line)
            {
                lines.Add(new SyncJournalRecord(line.Kind, line.Id, now));
            }
        }

        context.Set<SyncJournalRecord>().AddRange(lines);
    }
}

/// <summary>
/// Lit le journal de synchronisation du dossier courant. Jeton <c>j:{séquence}</c> après la première réception ;
/// sans jeton, instantané par pages (<c>s:{repère}:{nature}:{rang}</c>), puis journal à partir du repère pris au départ.
/// <para>
/// Les séquences d'identité peuvent se valider dans le désordre (PostgreSQL, SQL Server) : une séquence absente juste
/// avant une ligne récente peut appartenir à une transaction encore en cours. Le jeton s'arrête donc devant un trou
/// tant que la ligne qui le suit a moins de <see cref="GapGrace"/> ; au-delà, la transaction est tenue pour annulée.
/// </para>
/// </summary>
internal sealed class EfSyncChangeFeed(TenantDbSession session, TimeProvider clock) : ISyncChangeFeed
{
    public static readonly TimeSpan GapGrace = TimeSpan.FromSeconds(30);

    private static readonly SyncEntityKind[] SnapshotOrder = [SyncEntityKind.Customer, SyncEntityKind.Supplier, SyncEntityKind.CatalogItem];

    public async Task<Result<SyncChangesResponse>> ReadAsync(string? token, int limit, SyncFeedScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        limit = Math.Clamp(limit, 1, 1000);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(token))
        {
            long mark = await SafeWatermarkAsync(db, cancellationToken).ConfigureAwait(false);
            return await SnapshotAsync(db, mark, 0, 0, limit, scope, cancellationToken).ConfigureAwait(false);
        }

        string[] parts = token.Split(':');
        if (parts is ["j", var seq] && long.TryParse(seq, NumberStyles.None, CultureInfo.InvariantCulture, out long since))
        {
            return await JournalAsync(db, since, limit, scope, cancellationToken).ConfigureAwait(false);
        }

        if (parts is ["s", var m, var k, var o]
            && long.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out long watermark)
            && int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int kindIndex) && kindIndex < SnapshotOrder.Length
            && int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out int offset))
        {
            return await SnapshotAsync(db, watermark, kindIndex, offset, limit, scope, cancellationToken).ConfigureAwait(false);
        }

        return SyncErrors.TokenInvalid;
    }

    /// <summary>
    /// Pages par rang dans l'ordre des identifiants. Aucun élément n'est supprimé : une création pendant l'instantané ne
    /// peut que décaler la suite d'un rang (un élément envoyé deux fois), jamais en faire sauter un. Les éléments écrits
    /// après le repère arrivent de toute façon par le journal.
    /// </summary>
    private static async Task<Result<SyncChangesResponse>> SnapshotAsync(
        LettraDbContext db, long watermark, int kindIndex, int offset, int limit, SyncFeedScope scope, CancellationToken ct)
    {
        List<CustomerResponse> customers = [];
        List<SupplierResponse> suppliers = [];
        List<CatalogItemResponse> items = [];
        int remaining = limit;

        while (kindIndex < SnapshotOrder.Length && remaining > 0)
        {
            SyncEntityKind kind = SnapshotOrder[kindIndex];
            int read = 0;
            if (Allowed(kind, scope))
            {
                switch (kind)
                {
                    case SyncEntityKind.Customer:
                        List<Customer> c = await db.Customers.AsNoTracking().OrderBy(e => e.Id).Skip(offset).Take(remaining).ToListAsync(ct).ConfigureAwait(false);
                        customers.AddRange(c.Select(e => e.ToResponse()));
                        read = c.Count;
                        break;
                    case SyncEntityKind.Supplier:
                        List<Supplier> s = await db.Set<Supplier>().AsNoTracking().OrderBy(e => e.Id).Skip(offset).Take(remaining).ToListAsync(ct).ConfigureAwait(false);
                        suppliers.AddRange(s.Select(e => e.ToResponse()));
                        read = s.Count;
                        break;
                    case SyncEntityKind.CatalogItem:
                        List<CatalogItem> i = await db.Set<CatalogItem>().AsNoTracking().OrderBy(e => e.Id).Skip(offset).Take(remaining).ToListAsync(ct).ConfigureAwait(false);
                        items.AddRange(i.Select(e => e.ToResponse()));
                        read = i.Count;
                        break;
                }
            }

            remaining -= read;
            if (read > 0 && remaining == 0)
            {
                offset += read;
                break;
            }

            kindIndex++;
            offset = 0;
        }

        bool done = kindIndex >= SnapshotOrder.Length;
        string next = done
            ? Journal(watermark)
            : string.Create(CultureInfo.InvariantCulture, $"s:{watermark}:{kindIndex}:{offset}");
        return new SyncChangesResponse(next, !done, customers, suppliers, items);
    }

    private async Task<Result<SyncChangesResponse>> JournalAsync(LettraDbContext db, long since, int limit, SyncFeedScope scope, CancellationToken ct)
    {
        List<SyncJournalRecord> rows = await db.Set<SyncJournalRecord>().AsNoTracking()
            .Where(r => r.Sequence > since)
            .OrderBy(r => r.Sequence)
            .Take(limit + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        long recent = clock.GetUtcNow().Subtract(GapGrace).ToUnixTimeMilliseconds();
        long accepted = since;
        List<SyncJournalRecord> taken = [];
        bool blocked = false;
        foreach (SyncJournalRecord row in rows.Take(limit))
        {
            // Une séquence manque juste avant une ligne récente : elle peut encore se valider, on l'attend.
            if (row.Sequence != accepted + 1 && row.ChangedAtMs > recent)
            {
                blocked = true;
                break;
            }

            taken.Add(row);
            accepted = row.Sequence;
        }

        HashSet<Guid> Ids(SyncEntityKind kind) => [.. taken.Where(r => r.Entity == kind).Select(r => r.EntityId)];

        List<CustomerResponse> customers = [];
        List<SupplierResponse> suppliers = [];
        List<CatalogItemResponse> items = [];
        if (scope.Parties)
        {
            List<CustomerId> c = [.. Ids(SyncEntityKind.Customer).Select(id => new CustomerId(id))];
            if (c.Count > 0)
            {
                customers.AddRange((await db.Customers.AsNoTracking().Where(e => c.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false)).Select(e => e.ToResponse()));
            }

            List<SupplierId> s = [.. Ids(SyncEntityKind.Supplier).Select(id => new SupplierId(id))];
            if (s.Count > 0)
            {
                suppliers.AddRange((await db.Set<Supplier>().AsNoTracking().Where(e => s.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false)).Select(e => e.ToResponse()));
            }
        }

        if (scope.Catalog)
        {
            List<CatalogItemId> i = [.. Ids(SyncEntityKind.CatalogItem).Select(id => new CatalogItemId(id))];
            if (i.Count > 0)
            {
                items.AddRange((await db.Set<CatalogItem>().AsNoTracking().Where(e => i.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false)).Select(e => e.ToResponse()));
            }
        }

        bool hasMore = !blocked && rows.Count > limit;
        return new SyncChangesResponse(Journal(accepted), hasMore, customers, suppliers, items);
    }

    /// <summary>Plus haute séquence au-dessous de laquelle plus rien ne peut se valider : point de départ d'un instantané.</summary>
    private async Task<long> SafeWatermarkAsync(LettraDbContext db, CancellationToken ct)
    {
        long recent = clock.GetUtcNow().Subtract(GapGrace).ToUnixTimeMilliseconds();
        long mark = await db.Set<SyncJournalRecord>().Where(r => r.ChangedAtMs <= recent)
            .MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0;
        List<long> later = await db.Set<SyncJournalRecord>().Where(r => r.Sequence > mark)
            .OrderBy(r => r.Sequence).Select(r => r.Sequence).ToListAsync(ct).ConfigureAwait(false);
        foreach (long sequence in later)
        {
            if (sequence != mark + 1)
            {
                break;
            }

            mark = sequence;
        }

        return mark;
    }

    private static bool Allowed(SyncEntityKind kind, SyncFeedScope scope) =>
        kind == SyncEntityKind.CatalogItem ? scope.Catalog : scope.Parties;

    private static string Journal(long sequence) => string.Create(CultureInfo.InvariantCulture, $"j:{sequence}");
}

/// <summary>Réponses données par clé d'idempotence, dans un contexte à part : rien d'une commande échouée n'y est enregistré au passage.</summary>
internal sealed class EfSyncReceipts(
    Application.Abstractions.Tenancy.ITenantContext tenant,
    ITenantDbContextFactory factory,
    TenantMigrator migrator,
    PersistenceOptions options) : ISyncReceipts, IAsyncDisposable
{
    private readonly TenantDbSession _session = new(tenant, factory, migrator, options);

    public async Task<SyncReceipt?> FindAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        LettraDbContext db = await _session.GetAsync(cancellationToken).ConfigureAwait(false);
        SyncReceiptRecord? record = await db.Set<SyncReceiptRecord>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken).ConfigureAwait(false);
        return record?.ToReceipt();
    }

    public async Task SaveAsync(SyncReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        LettraDbContext db = await _session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<SyncReceiptRecord>().Add(new SyncReceiptRecord(receipt));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Deux envois simultanés de la même intention : la première réponse gardée suffit.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
