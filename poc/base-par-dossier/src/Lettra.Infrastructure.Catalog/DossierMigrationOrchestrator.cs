using System.Collections.Concurrent;
using System.Diagnostics;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Catalog;

public sealed record MigrationWaveOptions
{
    public int MaxParallelism { get; init; } = 16;
    /// Nombre maximal de bases dans la vague (canari : 1 % puis le reste). null = toutes.
    public int? Limit { get; init; }
    public string Owner { get; init; } = $"{Environment.MachineName}:{Environment.ProcessId}";
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(10);
    /// Au-delà de ce taux d'échec (après 20 bases), la vague s'arrête : on ne casse pas toute la cellule.
    public double MaxFailureRatio { get; init; } = 0.05;
}

public sealed record MigrationWaveReport(
    int Candidates, int Migrated, int Failed, int SkippedLeased, bool Aborted,
    TimeSpan Elapsed, double P50Ms, double P95Ms, double MaxMs, IReadOnlyList<(Guid DossierId, string Error)> Errors);

/// Migration progressive de toutes les bases de dossiers d'une cellule (Lettra.Worker).
/// Bail par dossier dans le catalogue : plusieurs Workers se répartissent le travail, et une vague
/// interrompue reprend simplement en relançant la commande (les bases déjà migrées sont ignorées).
public sealed class DossierMigrationOrchestrator(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IDossierDbContextFactory dossierFactory,
    TenantMigrator migrator,
    ILogger<DossierMigrationOrchestrator> logger)
{
    public async Task<MigrationWaveReport> RunWaveAsync(MigrationWaveOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var targets = new Dictionary<DatabaseProvider, string>();
        foreach (var p in Enum.GetValues<DatabaseProvider>())
            if (dossierFactory.LatestMigration(p) is { } latest)
                targets[p] = latest;

        List<Dossier> candidates;
        await using (var db = await catalogFactory.CreateDbContextAsync(ct))
        {
            var all = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.Active)
                .OrderBy(d => d.Id)
                .ToListAsync(ct);
            candidates = [.. all.Where(d => targets.TryGetValue(d.Provider, out var t) && d.SchemaVersion != t)];
            if (options.Limit is { } limit)
                candidates = [.. candidates.Take(limit)];
        }

        var durations = new ConcurrentBag<double>();
        var errors = new ConcurrentBag<(Guid, string)>();
        int migrated = 0, failed = 0, skipped = 0, attempted = 0;
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = abort.Token }, async (d, token) =>
            {
                var target = targets[d.Provider];
                if (!await TryAcquireLeaseAsync(d.Id, target, options, token))
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                var one = Stopwatch.StartNew();
                try
                {
                    var result = await migrator.MigrateAsync(TenantCatalog.ToDescriptor(d), stampIdentityIfMissing: false, token);
                    await ReleaseAsync(d.Id, options.Owner, result.To, null);
                    durations.Add(one.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref migrated);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Migration du dossier {DossierId} en échec", d.Id);
                    errors.Add((d.Id, ex.Message));
                    await ReleaseAsync(d.Id, options.Owner, null, DossierProvisioner.Truncate(ex.Message));
                    var f = Interlocked.Increment(ref failed);
                    var a = Volatile.Read(ref attempted);
                    if (a >= 20 && (double)f / a > options.MaxFailureRatio)
                        await abort.CancelAsync();
                }
                catch (OperationCanceledException)
                {
                    await ReleaseAsync(d.Id, options.Owner, null, null);
                    throw;
                }
                finally
                {
                    Interlocked.Increment(ref attempted);
                }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Vague arrêtée par le seuil d'échec.
        }

        var sorted = durations.Order().ToArray();
        return new MigrationWaveReport(candidates.Count, migrated, failed, skipped, abort.IsCancellationRequested && !ct.IsCancellationRequested,
            sw.Elapsed, Percentile(sorted, 0.50), Percentile(sorted, 0.95), sorted.LastOrDefault(), [.. errors]);
    }

    private async Task<bool> TryAcquireLeaseAsync(Guid dossierId, string target, MigrationWaveOptions options, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var n = await db.Dossiers
            .Where(d => d.Id == dossierId && d.State == DossierState.Active && d.SchemaVersion != target
                        && (d.LeaseUntil == null || d.LeaseUntil < now || d.LeaseOwner == options.Owner))
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LeaseOwner, options.Owner)
                .SetProperty(d => d.LeaseUntil, now + options.LeaseDuration), ct);
        return n == 1;
    }

    private async Task ReleaseAsync(Guid dossierId, string owner, string? newVersion, string? error)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(CancellationToken.None);
        var q = db.Dossiers.Where(d => d.Id == dossierId && d.LeaseOwner == owner);
        if (newVersion is not null)
            await q.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.SchemaVersion, newVersion)
                .SetProperty(d => d.LeaseOwner, (string?)null)
                .SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(d => d.LastError, (string?)null)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow));
        else
            await q.ExecuteUpdateAsync(s => s
                .SetProperty(d => d.LeaseOwner, (string?)null)
                .SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(d => d.LastError, error)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow));
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
}
