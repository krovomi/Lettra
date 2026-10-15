using System.Globalization;
using Lettra.Application.Abstractions.Sync;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
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
