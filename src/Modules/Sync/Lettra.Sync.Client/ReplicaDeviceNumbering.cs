using Lettra.Application.Abstractions.Sync;

namespace Lettra.Sync.Client;

/// <summary>Sur le poste : la validation numérote dans la série du poste dès qu'il en a reçu une (ADR-0007).</summary>
internal sealed class ReplicaDeviceNumbering(ISyncReplica replica) : IDeviceNumbering
{
    public Task<DeviceSeries?> FindAsync(string baseSeries, CancellationToken cancellationToken) =>
        replica.FindDeviceSeriesAsync(baseSeries, cancellationToken);
}
