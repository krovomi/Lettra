using System.Collections.Concurrent;
using System.Diagnostics;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Catalog;

public sealed record MigrationWaveOptions
{
    public int MaxParallelism { get; init; } = 16;

    /// <summary>Nombre maximal de bases dans la vague (canari d'abord, puis le reste). <c>null</c> : toutes.</summary>
    public int? Limit { get; init; }

    /// <summary>Identité du Worker qui prend les baux.</summary>
    public string Owner { get; init; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Au-delà de ce taux d'échec (après 20 bases), la vague s'arrête : on ne casse pas toute la cellule.</summary>
    public double MaxFailureRatio { get; init; } = 0.05;
}

public sealed record MigrationWaveReport(
    int Candidates,
    int Migrated,
    int Failed,
    int SkippedLeased,
    bool Aborted,
    TimeSpan Elapsed);

/// <summary>
/// Migration progressive de toutes les bases de dossiers (Lettra.Worker). Un bail par dossier dans le catalogue
/// répartit le travail entre plusieurs Workers ; une vague interrompue reprend en la relançant : les bases déjà à
/// jour ne sont plus candidates. EF Core 10 pose en plus son propre verrou de migration dans chaque base.
/// </summary>
public sealed partial class DossierMigrationOrchestrator(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IDossierDbContextFactory dossierFactory,
    TenantMigrator migrator,
    TimeProvider clock,
    ILogger<DossierMigrationOrchestrator> logger)
{
    private const int MinAttemptsBeforeAbort = 20;

    public async Task<MigrationWaveReport> RunWaveAsync(MigrationWaveOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        Stopwatch elapsed = Stopwatch.StartNew();
        Dictionary<DatabaseProvider, string> targets = [];
        foreach (DatabaseProvider provider in Enum.GetValues<DatabaseProvider>())
        {
            if (dossierFactory.LatestMigration(provider) is { } latest)
            {
                targets[provider] = latest;
            }
        }

        List<CatalogDossier> candidates = await CandidatesAsync(targets, options.Limit, cancellationToken).ConfigureAwait(false);
        int migrated = 0, failed = 0, skipped = 0, attempted = 0;
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel.ForEachAsync(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = abort.Token },
                async (dossier, token) =>
                {
                    string target = targets[dossier.Provider];
                    if (!await TryAcquireLeaseAsync(dossier.Id, target, options, token).ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    try
                    {
                        DossierMigrationResult result = await migrator.MigrateAsync(dossier.ToDescriptor(), stampIfMissing: false, token).ConfigureAwait(false);
                        await ReleaseAsync(dossier.Id, options.Owner, result.To, error: null).ConfigureAwait(false);
                        Interlocked.Increment(ref migrated);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        LogMigrationFailed(logger, exception, dossier.Id);
                        await ReleaseAsync(dossier.Id, options.Owner, newVersion: null, CatalogDossierProvisioner.Truncate(exception.Message)).ConfigureAwait(false);
                        int failures = Interlocked.Increment(ref failed);
                        int attempts = Volatile.Read(ref attempted) + 1;
                        if (attempts >= MinAttemptsBeforeAbort && (double)failures / attempts > options.MaxFailureRatio)
                        {
                            await abort.CancelAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        await ReleaseAsync(dossier.Id, options.Owner, newVersion: null, error: null).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        Interlocked.Increment(ref attempted);
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Vague arrêtée par le seuil d'échec : les bases restantes attendent la prochaine vague.
        }

        bool aborted = abort.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
        if (aborted)
        {
            LogWaveAborted(logger, failed, attempted);
        }

        return new MigrationWaveReport(candidates.Count, migrated, failed, skipped, aborted, elapsed.Elapsed);
    }

    private async Task<List<CatalogDossier>> CandidatesAsync(Dictionary<DatabaseProvider, string> targets, int? limit, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<CatalogDossier> active = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.Active)
                .OrderBy(d => d.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<CatalogDossier> outdated = active.Where(d => targets.TryGetValue(d.Provider, out string? target) && d.SchemaVersion != target);
            return [.. limit is int max ? outdated.Take(max) : outdated];
        }
    }

    private async Task<bool> TryAcquireLeaseAsync(Guid dossierId, string target, MigrationWaveOptions options, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime until = now + options.LeaseDuration;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            int taken = await db.Dossiers
                .Where(d => d.Id == dossierId && d.State == DossierState.Active && d.SchemaVersion != target
                    && (d.LeaseUntil == null || d.LeaseUntil < now || d.LeaseOwner == options.Owner))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LeaseOwner, options.Owner).SetProperty(d => d.LeaseUntil, until), cancellationToken)
                .ConfigureAwait(false);
            return taken == 1;
        }
    }

    private async Task ReleaseAsync(Guid dossierId, string owner, string? newVersion, string? error)
    {
        DateTimeOffset now = clock.GetUtcNow();
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Dossiers.Where(d => d.Id == dossierId && d.LeaseOwner == owner).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.SchemaVersion, d => newVersion ?? d.SchemaVersion)
                    .SetProperty(d => d.LeaseOwner, (string?)null)
                    .SetProperty(d => d.LeaseUntil, (DateTime?)null)
                    .SetProperty(d => d.LastError, error)
                    .SetProperty(d => d.UpdatedAt, now)).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration de la base du dossier {DossierId} en échec")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception, Guid dossierId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vague de migration arrêtée : {Failed} échecs sur {Attempted} bases")]
    private static partial void LogWaveAborted(ILogger logger, int failed, int attempted);
}
