using System.Globalization;
using Lettra.Application.Abstractions.Sync;
using Lettra.Domain.Common;

namespace Lettra.Sync.Client;

/// <summary>
/// Sur le poste : la validation numérote dans la série du poste dès qu'il en a reçu une (ADR-0007). Elle est refusée
/// quand le poste est révoqué, ou quand son dernier contact avec le serveur est plus ancien que la durée maximale hors
/// ligne fixée par le cabinet (étape 4.4) : une pièce validée doit pouvoir partir à la plateforme agréée sans tarder.
/// </summary>
internal sealed class ReplicaDeviceNumbering(ISyncReplica replica, TimeProvider clock) : IDeviceNumbering
{
    public async Task<Result<DeviceSeries?>> FindAsync(string baseSeries, CancellationToken cancellationToken)
    {
        if (await replica.GetSettingAsync(DeviceSettings.RevokedKey, cancellationToken).ConfigureAwait(false) is not null)
        {
            return SyncErrors.DeviceRevoked;
        }

        DeviceSeries? series = await replica.FindDeviceSeriesAsync(baseSeries, cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return Result<DeviceSeries?>.Success(null);
        }

        // Une série n'arrive qu'en ligne : le poste qui en a une a toujours un dernier contact.
        DateTimeOffset? lastContact = Millis(await replica.GetSettingAsync(DeviceSettings.LastContactKey, cancellationToken).ConfigureAwait(false));
        int hours = int.TryParse(await replica.GetSettingAsync(DeviceSettings.MaxOfflineHoursKey, cancellationToken).ConfigureAwait(false),
            NumberStyles.None, CultureInfo.InvariantCulture, out int stored) && OfflinePolicy.IsValid(stored)
            ? stored
            : OfflinePolicy.DefaultMaxOfflineHours;
        if (lastContact is null || clock.GetUtcNow() - lastContact.Value > TimeSpan.FromHours(hours))
        {
            return SyncErrors.OfflineTooLong;
        }

        return series;
    }

    internal static DateTimeOffset? Millis(string? stored) =>
        long.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out long ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
}
