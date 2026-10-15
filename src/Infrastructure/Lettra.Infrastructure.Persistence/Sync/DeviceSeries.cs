using System.Globalization;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Numbering;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>
/// Une série de poste (ADR-0007). Sur le serveur, c'est le registre : quel poste numérote dans quelle série. Sur un
/// poste, il n'y a que la sienne. Le compteur lui-même est une <see cref="NumberSeries"/> ordinaire du même code, ce
/// qui garde la réservation du numéro et le contrôle d'intégrité par série inchangés.
/// </summary>
internal sealed class DeviceSeriesRecord
{
    private DeviceSeriesRecord()
    {
    }

    public DeviceSeriesRecord(string code, string baseSeries, Guid deviceId, DateTimeOffset openedAt)
    {
        Code = code;
        BaseSeries = baseSeries;
        DeviceId = deviceId;
        OpenedAtMs = openedAt.ToUnixTimeMilliseconds();
    }

    public string Code { get; private set; } = string.Empty;

    public string BaseSeries { get; private set; } = string.Empty;

    public Guid DeviceId { get; private set; }

    public long OpenedAtMs { get; private set; }

    /// <summary>Série clôturée (poste perdu ou réinstallé, phase 4.4) : plus aucune pièce n'y est reçue.</summary>
    public long? ClosedAtMs { get; private set; }
}

internal sealed class DeviceSeriesRecordConfiguration : IEntityTypeConfiguration<DeviceSeriesRecord>
{
    public void Configure(EntityTypeBuilder<DeviceSeriesRecord> builder)
    {
        builder.ToTable("DeviceSeries");
        builder.HasKey(r => r.Code);
        builder.Property(r => r.Code).HasMaxLength(16);
        builder.Property(r => r.BaseSeries).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => new { r.DeviceId, r.BaseSeries });
    }
}

/// <summary>Ce que les deux côtés partagent : lire une série de poste avec son compteur, en créer une.</summary>
internal static class DeviceSeriesStore
{
    public static string CodeOf(string baseSeries, int ordinal) =>
        string.Create(CultureInfo.InvariantCulture, $"{baseSeries}-P{ordinal:00}");

    public static async Task<DeviceSeries?> FindOpenAsync(LettraDbContext db, Guid? deviceId, string baseSeries, CancellationToken cancellationToken)
    {
        IQueryable<DeviceSeriesRecord> open = db.Set<DeviceSeriesRecord>().AsNoTracking()
            .Where(r => r.BaseSeries == baseSeries && r.ClosedAtMs == null);
        if (deviceId is Guid device)
        {
            open = open.Where(r => r.DeviceId == device);
        }

        var found = await open
            .Join(db.NumberSeries.AsNoTracking(), r => r.Code, s => s.Code, (r, s) => new { r.Code, r.BaseSeries, s.Prefix, r.DeviceId, s.Next, r.OpenedAtMs })
            .OrderByDescending(x => x.OpenedAtMs)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return found is null ? null : new DeviceSeries(found.BaseSeries, found.Code, found.Prefix, found.DeviceId, found.Next);
    }

    /// <summary>Ajoute la série et son compteur, positionné sur <paramref name="next"/>, en un seul enregistrement.</summary>
    public static async Task AddAsync(LettraDbContext db, DeviceSeriesRecord record, string prefix, long next, CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            (db, record, prefix, next),
            static async (_, state, token) =>
            {
                await using IDbContextTransaction transaction = await state.db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                state.db.Set<DeviceSeriesRecord>().Add(state.record);
                state.db.NumberSeries.Add(new NumberSeries(state.record.Code, state.prefix));
                await state.db.SaveChangesAsync(token).ConfigureAwait(false);
                if (state.next > 1)
                {
                    await state.db.NumberSeries.Where(s => s.Code == state.record.Code)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Next, state.next), token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Registre des séries de poste du dossier, côté serveur. Un poste reçoit une série par série du dossier au premier
/// passage en ligne ; ses pièces validées hors ligne n'y sont reçues que dans l'ordre exact de leurs numéros.
/// </summary>
internal sealed class EfDeviceSeriesRegistry(TenantDbSession session) : IDeviceSeriesRegistry
{
    private const int MaxOpenAttempts = 10;

    public async Task<DeviceSeries> OpenAsync(Guid deviceId, string baseSeries, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSeries);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        for (int attempt = 1; ; attempt++)
        {
            if (await DeviceSeriesStore.FindOpenAsync(db, deviceId, baseSeries, cancellationToken).ConfigureAwait(false) is { } open)
            {
                return open;
            }

            if (!await db.NumberSeries.AsNoTracking().AnyAsync(s => s.Code == baseSeries, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"La série {baseSeries} n'existe pas dans ce dossier.");
            }

            // Les séries ne sont jamais supprimées (une série close le reste) : leur nombre donne le rang suivant. Deux
            // ouvertures simultanées visent le même code ; la clé primaire en refuse une, qui recommence.
            int ordinal = await db.Set<DeviceSeriesRecord>().CountAsync(r => r.BaseSeries == baseSeries, cancellationToken).ConfigureAwait(false) + 1;
            string code = DeviceSeriesStore.CodeOf(baseSeries, ordinal);
            try
            {
                await DeviceSeriesStore.AddAsync(db, new DeviceSeriesRecord(code, baseSeries, deviceId, now), code + "-", 1, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) when (attempt < MaxOpenAttempts)
            {
                db.ChangeTracker.Clear();
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<Result<T>> ExecuteWithDeviceNumberAsync<T>(
        string seriesCode,
        Guid deviceId,
        string number,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, seriesCode, deviceId, number, work),
            static (_, state, token) => TryOnceAsync(state.db, state.seriesCode, state.deviceId, state.number, state.work, token),
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<T>> TryOnceAsync<T>(
        LettraDbContext db,
        string seriesCode,
        Guid deviceId,
        string number,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool owned = await db.Set<DeviceSeriesRecord>().AsNoTracking()
            .AnyAsync(r => r.Code == seriesCode && r.DeviceId == deviceId && r.ClosedAtMs == null, cancellationToken).ConfigureAwait(false);
        var series = owned
            ? await db.NumberSeries.AsNoTracking().Where(s => s.Code == seriesCode)
                .Select(s => new { s.Prefix, s.Next, s.Version })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (series is null)
        {
            return SyncErrors.DeviceSeriesUnknown;
        }

        if (!string.Equals(NumberSeries.Format(series.Prefix, series.Next), number, StringComparison.Ordinal))
        {
            return SyncErrors.NumberOutOfOrder;
        }

        // Seul ce poste écrit dans sa série : la version ne protège ici que contre deux envois simultanés du même lot.
        int reserved = await db.NumberSeries
            .Where(s => s.Code == seriesCode && s.Version == series.Version)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Next, x => x.Next + 1).SetProperty(x => x.Version, x => x.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        if (reserved == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return NumberingErrors.Contention;
        }

        Result<T> result = await work(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return result;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("Écriture concurrente détectée à l'enregistrement.", ex);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
