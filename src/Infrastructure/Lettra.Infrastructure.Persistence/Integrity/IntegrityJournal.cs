using System.Runtime.CompilerServices;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lettra.Infrastructure.Persistence.Integrity;

/// <summary>
/// Ajout en tête de chaîne. La tête est verrouillée par un <c>UPDATE</c> (verrou de ligne en PostgreSQL et SQL Server,
/// verrou d'écriture de la base en SQLite) avant d'être lue : deux ajouts concurrents du même dossier se suivent, ils
/// ne se chevauchent pas. Dans la transaction de validation, ce verrou est pris après celui de la série : pas
/// d'interblocage possible.
/// </summary>
internal sealed class EfIntegrityJournal(TenantDbSession session, ITenantContext tenant, EnvelopedIntegrityKeyring keyring, TimeProvider clock)
    : IIntegrityJournal
{
    public async Task<Result<long>> AppendAsync(DocumentLifecycleEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        if (db.Database.CurrentTransaction is not null)
        {
            // Dans la transaction de l'appelant : c'est lui qui enregistre, avec sa pièce.
            Result<JournalEntry> appended = await ChainWriter.AppendAsync(
                db, tenant.Current, keyring, clock, (sequence, previous, at) => JournalEntry.ForDocument(@event, sequence, previous, at), cancellationToken).ConfigureAwait(false);
            return appended.Map(e => e.Sequence);
        }

        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            async (_, db, token) =>
            {
                db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                Result<JournalEntry> appended = await ChainWriter.AppendAsync(
                    db, tenant.Current, keyring, clock, (sequence, previous, at) => JournalEntry.ForDocument(@event, sequence, previous, at), token).ConfigureAwait(false);
                if (appended.IsFailure)
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return Result<long>.Failure(appended.Errors);
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return Result<long>.Success(appended.Value.Sequence);
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Écriture en tête de chaîne, sous le verrou de la tête, dans la transaction ouverte. La clé en vigueur est lue sous
/// ce verrou : une rotation concurrente passe avant ou après, jamais au milieu.
/// </summary>
internal static class ChainWriter
{
    public static async Task<Result<JournalEntry>> AppendAsync(
        LettraDbContext db,
        TenantDescriptor tenant,
        EnvelopedIntegrityKeyring keyring,
        TimeProvider clock,
        Func<long, string, DateTimeOffset, Result<JournalEntry>> build,
        CancellationToken cancellationToken)
    {
        await LockHeadAsync(db, cancellationToken).ConfigureAwait(false);
        Result<IntegrityKeyMaterial> keys = await keyring.CurrentAsync(db, cancellationToken).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            return Result<JournalEntry>.Failure(keys.Errors);
        }

        var head = await db.IntegrityChainHead.AsNoTracking()
            .Where(h => h.Id == IntegrityChainHead.SingletonId)
            .Select(h => new { h.LastSequence, h.LastHash })
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        string previous = head.LastSequence == 0 ? JournalEntry.GenesisHash(tenant.DossierId) : head.LastHash;
        Result<JournalEntry> built = build(head.LastSequence + 1, previous, clock.GetUtcNow());
        if (built.IsFailure)
        {
            return built;
        }

        JournalEntry entry = built.Value;
        entry.ApplySeal(keys.Value.KeyId, keys.Value.Seal(entry.Hash));
        db.IntegrityJournal.Add(entry);
        await db.IntegrityChainHead
            .Where(h => h.Id == IntegrityChainHead.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.LastSequence, entry.Sequence).SetProperty(h => h.LastHash, entry.Hash), cancellationToken)
            .ConfigureAwait(false);
        return entry;
    }

    public static async Task LockHeadAsync(LettraDbContext db, CancellationToken cancellationToken)
    {
        int locked = await db.IntegrityChainHead
            .Where(h => h.Id == IntegrityChainHead.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Version, h => h.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        if (locked == 0)
        {
            throw new InvalidOperationException("Tête de la chaîne d'intégrité absente : base de dossier non migrée.");
        }
    }
}

/// <summary>Lecture du journal et des clôtures, et clôture d'une période.</summary>
internal sealed class EfIntegrityStore(TenantDbSession session, ITenantContext tenant, EnvelopedIntegrityKeyring keyring, TimeProvider clock)
    : IIntegrityStore
{
    internal const int BatchSize = 1000;

    public async Task<IReadOnlyList<PeriodClosing>> ListClosingsAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.IntegrityClosings.AsNoTracking().OrderBy(c => c.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<JournalEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        long after = 0;
        while (true)
        {
            long from = after;
            List<JournalEntry> batch = await db.IntegrityJournal.AsNoTracking()
                .Where(e => e.Sequence > from)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (JournalEntry entry in batch)
            {
                yield return entry;
            }

            if (batch.Count < BatchSize)
            {
                yield break;
            }

            after = batch[^1].Sequence;
        }
    }

    public async Task<DateTimeOffset?> FirstEntryAtAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        JournalEntry? first = await db.IntegrityJournal.AsNoTracking().OrderBy(e => e.Sequence).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return first?.OccurredAt;
    }

    public async Task<IReadOnlyDictionary<string, long>> NextNumbersAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.NumberSeries.AsNoTracking().ToDictionaryAsync(s => s.Code, s => s.Next, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PeriodClosing>> CloseAsync(ClosingPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, period),
            (_, state, token) => CloseOnceAsync(state.db, state.period, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IntegrityKeyVersion>> RotateKeysAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            (_, db, token) => RotateOnceAsync(db, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IntegrityKeyVersion>> RotateOnceAsync(LettraDbContext db, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ChainWriter.LockHeadAsync(db, cancellationToken).ConfigureAwait(false);

        // La version en vigueur (créée ici si le dossier n'en a pas encore) scelle l'annonce de la suivante.
        Result<IntegrityKeyMaterial> current = await keyring.CurrentAsync(db, cancellationToken).ConfigureAwait(false);
        Result<(IntegrityKeyMaterial Keys, StoredIntegrityKey Row)> next = current.IsFailure
            ? Result<(IntegrityKeyMaterial, StoredIntegrityKey)>.Failure(current.Errors)
            : await keyring.PrepareNextAsync(db, cancellationToken).ConfigureAwait(false);
        Result<JournalEntry> announced = next.IsFailure
            ? Result<JournalEntry>.Failure(next.Errors)
            : await ChainWriter.AppendAsync(
                db, tenant.Current, keyring, clock,
                (sequence, previousHash, at) => JournalEntry.ForKeyRotation(next.Value.Keys.KeyId, next.Value.Keys.PublicKey, sequence, previousHash, at),
                cancellationToken).ConfigureAwait(false);
        if (announced.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<IntegrityKeyVersion>.Failure(announced.Errors);
        }

        StoredIntegrityKey row = next.Value.Row;
        db.IntegrityKeys.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new IntegrityKeyVersion(row.Version, row.KeyId, row.PublicKey, row.CreatedAt, null);
    }

    private async Task<Result<PeriodClosing>> CloseOnceAsync(LettraDbContext db, ClosingPeriod period, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ChainWriter.LockHeadAsync(db, cancellationToken).ConfigureAwait(false);
        Result<IntegrityKeyMaterial> keys = await keyring.CurrentAsync(db, cancellationToken).ConfigureAwait(false);
        if (keys.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<PeriodClosing>.Failure(keys.Errors);
        }

        PeriodClosing? previous = await db.IntegrityClosings.AsNoTracking()
            .OrderByDescending(c => c.Number).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (previous is not null && previous.PeriodEnd >= period.End)
        {
            // Déjà clôturée (un autre Worker ou l'utilisateur est passé avant) : on rend la clôture existante.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return previous;
        }

        long after = previous?.LastSequence ?? 0;
        List<JournalEntry> candidates = await db.IntegrityJournal.AsNoTracking()
            .Where(e => e.Sequence > after && e.OccurredAt < period.EndsAt)
            .OrderBy(e => e.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Une plage continue : on s'arrête au premier trou (une entrée plus récente que la fin, enregistrée avant).
        List<JournalEntry> covered = [];
        foreach (JournalEntry entry in candidates)
        {
            if (entry.Sequence != after + covered.Count + 1)
            {
                break;
            }

            covered.Add(entry);
        }

        string lastKnownHash = previous?.LastEntryHash ?? JournalEntry.GenesisHash(tenant.Current.DossierId);
        PeriodClosing closing = PeriodClosing.Prepare(
            tenant.Current.DossierId, previous, period.Kind, period.Start, period.End, covered, lastKnownHash, clock.GetUtcNow(), keys.Value.KeyId, keys.Value.PublicKey);
        closing.ApplySignature(keys.Value.Sign(closing.Hash));
        db.IntegrityClosings.Add(closing);

        Result<JournalEntry> recorded = await ChainWriter.AppendAsync(
            db, tenant.Current, keyring, clock, (sequence, previousHash, at) => JournalEntry.ForClosing(closing, sequence, previousHash, at), cancellationToken).ConfigureAwait(false);
        if (recorded.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<PeriodClosing>.Failure(recorded.Errors);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return closing;
    }
}
