using System.Globalization;
using Lettra.Application.Abstractions.Sync;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence.Numbering;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>Un poste qui s'est synchronisé avec le dossier (registre du serveur, étape 4.4).</summary>
internal sealed class SyncDeviceRecord
{
    private SyncDeviceRecord()
    {
    }

    public SyncDeviceRecord(Guid deviceId, Guid userId, DateTimeOffset now)
    {
        DeviceId = deviceId;
        FirstSeenAtMs = now.ToUnixTimeMilliseconds();
        LastSeenAtMs = FirstSeenAtMs;
        LastUserId = userId;
    }

    public Guid DeviceId { get; private set; }

    public long FirstSeenAtMs { get; private set; }

    public long LastSeenAtMs { get; private set; }

    public Guid? LastUserId { get; private set; }

    public long? RevokedAtMs { get; private set; }

    public Guid? RevokedBy { get; private set; }

    /// <summary>Premier contact après la révocation : le poste a reçu l'ordre d'effacer sa réplique.</summary>
    public long? WipedAtMs { get; private set; }

    public void Seen(Guid userId, DateTimeOffset now)
    {
        LastSeenAtMs = now.ToUnixTimeMilliseconds();
        LastUserId = userId;
    }

    public void Revoke(Guid by, DateTimeOffset now)
    {
        RevokedAtMs ??= now.ToUnixTimeMilliseconds();
        RevokedBy ??= by;
    }

    public void Wiped(DateTimeOffset now) => WipedAtMs ??= now.ToUnixTimeMilliseconds();

    public SyncDeviceResponse ToResponse() => new(
        DeviceId,
        DateTimeOffset.FromUnixTimeMilliseconds(FirstSeenAtMs),
        DateTimeOffset.FromUnixTimeMilliseconds(LastSeenAtMs),
        LastUserId,
        RevokedAtMs is long revoked ? DateTimeOffset.FromUnixTimeMilliseconds(revoked) : null,
        RevokedBy,
        WipedAtMs is long wiped ? DateTimeOffset.FromUnixTimeMilliseconds(wiped) : null);
}

internal sealed class SyncDeviceRecordConfiguration : IEntityTypeConfiguration<SyncDeviceRecord>
{
    public void Configure(EntityTypeBuilder<SyncDeviceRecord> builder)
    {
        builder.ToTable("SyncDevices");
        builder.HasKey(r => r.DeviceId);
        builder.Property(r => r.DeviceId).ValueGeneratedNever();
    }
}

/// <summary>
/// Déblocage d'une série de poste (étape 4.4) : le numéro attendu sera reçu sous l'autorité de qui l'a débloqué. Une
/// ligne par numéro débloqué, gardée après usage comme trace.
/// </summary>
internal sealed class SyncSeriesReleaseRecord
{
    private SyncSeriesReleaseRecord()
    {
    }

    public SyncSeriesReleaseRecord(string number, string seriesCode, Guid deviceId, Guid releasedBy, DateTimeOffset now)
    {
        Number = number;
        SeriesCode = seriesCode;
        DeviceId = deviceId;
        ReleasedBy = releasedBy;
        ReleasedAtMs = now.ToUnixTimeMilliseconds();
    }

    public string Number { get; private set; } = string.Empty;

    public string SeriesCode { get; private set; } = string.Empty;

    public Guid DeviceId { get; private set; }

    public Guid ReleasedBy { get; private set; }

    public long ReleasedAtMs { get; private set; }

    /// <summary>La pièce débloquée est reçue.</summary>
    public long? ConsumedAtMs { get; private set; }

    public void Consume(DateTimeOffset now) => ConsumedAtMs ??= now.ToUnixTimeMilliseconds();
}

internal sealed class SyncSeriesReleaseRecordConfiguration : IEntityTypeConfiguration<SyncSeriesReleaseRecord>
{
    public void Configure(EntityTypeBuilder<SyncSeriesReleaseRecord> builder)
    {
        builder.ToTable("SyncSeriesReleases");
        builder.HasKey(r => r.Number);
        builder.Property(r => r.Number).HasMaxLength(32);
        builder.Property(r => r.SeriesCode).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => r.DeviceId);
    }
}

/// <summary>
/// Registre des postes du dossier, et règle de durée hors ligne (gardée dans <c>SyncState</c>, comme les réglages du
/// poste). Un contact met le poste à jour ; un poste révoqué est refusé à chaque contact, le premier refus étant noté
/// comme l'effacement de sa réplique.
/// </summary>
internal sealed class EfSyncDeviceRegistry(TenantDbSession session) : ISyncDeviceRegistry
{
    internal const string MaxOfflineHoursKey = "server.max-offline-hours";

    public async Task<Result> TouchAsync(Guid deviceId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        SyncDeviceRecord? device = await db.Set<SyncDeviceRecord>().AsTracking()
            .FirstOrDefaultAsync(r => r.DeviceId == deviceId, cancellationToken).ConfigureAwait(false);
        Result result = Result.Success();
        if (device is null)
        {
            db.Set<SyncDeviceRecord>().Add(new SyncDeviceRecord(deviceId, userId, now));
        }
        else if (device.RevokedAtMs is not null)
        {
            device.Wiped(now);
            result = SyncErrors.DeviceRevoked;
        }
        else
        {
            device.Seen(userId, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (device is null)
        {
            // Premier contact simultané par deux appels : l'autre l'a inscrit.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return result;
    }

    public async Task<IReadOnlyList<SyncDeviceResponse>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<SyncDeviceRecord> rows = await db.Set<SyncDeviceRecord>().AsNoTracking()
            .OrderByDescending(r => r.LastSeenAtMs).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToResponse())];
    }

    public async Task<bool> RevokeAsync(Guid deviceId, Guid revokedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        SyncDeviceRecord? device = await db.Set<SyncDeviceRecord>().AsTracking()
            .FirstOrDefaultAsync(r => r.DeviceId == deviceId, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return false;
        }

        device.Revoke(revokedBy, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return true;
    }

    public async Task<Result<ReleaseDeviceSeriesResponse>> ReleaseNextAsync(Guid deviceId, Guid releasedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        if (!await db.Set<SyncDeviceRecord>().AsNoTracking().AnyAsync(r => r.DeviceId == deviceId, cancellationToken).ConfigureAwait(false))
        {
            return SyncErrors.DeviceUnknown;
        }

        // Le numéro attendu est le suivant du compteur de la série au serveur : celui de la pièce qui bloque.
        var open = await db.Set<DeviceSeriesRecord>().AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ClosedAtMs == null)
            .Join(db.NumberSeries.AsNoTracking(), r => r.Code, s => s.Code, (r, s) => new { r.Code, s.Prefix, s.Next })
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (open is null)
        {
            return SyncErrors.NothingToRelease;
        }

        string number = NumberSeries.Format(open.Prefix, open.Next);
        if (!await db.Set<SyncSeriesReleaseRecord>().AsNoTracking().AnyAsync(r => r.Number == number, cancellationToken).ConfigureAwait(false))
        {
            db.Set<SyncSeriesReleaseRecord>().Add(new SyncSeriesReleaseRecord(number, open.Code, deviceId, releasedBy, now));
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Débloqué en même temps par un autre manager : un seul déblocage compte.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        return new ReleaseDeviceSeriesResponse(open.Code, number);
    }

    public async Task<Guid?> FindReleaseAsync(Guid deviceId, string number, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SyncSeriesReleaseRecord>().AsNoTracking()
            .Where(r => r.Number == number && r.DeviceId == deviceId && r.ConsumedAtMs == null)
            .Select(r => (Guid?)r.ReleasedBy)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConsumeReleaseAsync(Guid deviceId, string number, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        long at = now.ToUnixTimeMilliseconds();
        await db.Set<SyncSeriesReleaseRecord>()
            .Where(r => r.Number == number && r.DeviceId == deviceId && r.ConsumedAtMs == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAtMs, at), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListReleasesAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<SyncSeriesReleaseRecord>().AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.ConsumedAtMs == null)
            .OrderBy(r => r.Number)
            .Select(r => r.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetMaxOfflineHoursAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        string? stored = await db.Set<SyncStateRecord>().AsNoTracking()
            .Where(r => r.Key == MaxOfflineHoursKey).Select(r => r.Value)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out int hours) && OfflinePolicy.IsValid(hours)
            ? hours
            : OfflinePolicy.DefaultMaxOfflineHours;
    }

    public async Task SaveMaxOfflineHoursAsync(int hours, CancellationToken cancellationToken)
    {
        if (!OfflinePolicy.IsValid(hours))
        {
            throw new ArgumentOutOfRangeException(nameof(hours), hours, "Durée maximale hors ligne hors des bornes.");
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        string value = hours.ToString(CultureInfo.InvariantCulture);
        SyncStateRecord? row = await db.Set<SyncStateRecord>().AsTracking()
            .FirstOrDefaultAsync(r => r.Key == MaxOfflineHoursKey, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.Set<SyncStateRecord>().Add(new SyncStateRecord(MaxOfflineHoursKey, value));
        }
        else
        {
            row.Set(value);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }
}
