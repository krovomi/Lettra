using Lettra.Supervision.Infrastructure;
using Lettra.Infrastructure.Catalog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Worker;

public sealed class DossierMigrationJobOptions
{
    public const string SectionName = "Lettra:Worker";

    /// <summary>Une seule passe puis arrêt de l'hôte (job Kubernetes lancé par le déploiement). Sinon, une passe par intervalle.</summary>
    public bool RunOnce { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Bases migrées en premier ; la suite de la vague n'a lieu que si le canari n'a pas été arrêté.</summary>
    public int CanarySize { get; set; } = 25;

    public int MaxParallelism { get; set; } = 16;
}

/// <summary>
/// Une passe : migre le catalogue, reprend les créations de dossiers interrompues, puis migre les bases de dossiers
/// en retard (canari, puis le reste). Plusieurs Workers peuvent tourner : chaque base est prise sous bail.
/// </summary>
internal sealed partial class DossierMigrationJob(
    IServiceProvider services,
    DossierMigrationOrchestrator orchestrator,
    IOptions<DossierMigrationJobOptions> options,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<DossierMigrationJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DossierMigrationJobOptions settings = options.Value;
        using PeriodicTimer timer = new(settings.Interval, clock);
        do
        {
            bool healthy = await RunPassAsync(settings, stoppingToken).ConfigureAwait(false);
            if (settings.RunOnce)
            {
                Environment.ExitCode = healthy ? 0 : 1;
                lifetime.StopApplication();
                return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<bool> RunPassAsync(DossierMigrationJobOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            await services.InitializeCatalogAsync(migrate: true, cancellationToken).ConfigureAwait(false);
            await services.MigrateSupervisionAsync(cancellationToken).ConfigureAwait(false);
            int resumed = await services.ResumeDossierProvisioningAsync(cancellationToken).ConfigureAwait(false);
            if (resumed > 0)
            {
                LogResumed(logger, resumed);
            }

            MigrationWaveReport canary = await orchestrator.RunWaveAsync(
                new MigrationWaveOptions { Limit = settings.CanarySize, MaxParallelism = settings.MaxParallelism }, cancellationToken).ConfigureAwait(false);
            LogWave(logger, "canari", canary.Candidates, canary.Migrated, canary.Failed, canary.SkippedLeased, canary.Elapsed);
            if (canary.Aborted || canary.Failed > 0)
            {
                // Un échec dans le canari arrête la passe : on n'étend pas une migration qui casse.
                return false;
            }

            MigrationWaveReport rest = await orchestrator.RunWaveAsync(
                new MigrationWaveOptions { MaxParallelism = settings.MaxParallelism }, cancellationToken).ConfigureAwait(false);
            LogWave(logger, "complète", rest.Candidates, rest.Migrated, rest.Failed, rest.SkippedLeased, rest.Elapsed);
            return !rest.Aborted && rest.Failed == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(logger, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} créations de dossiers reprises")]
    private static partial void LogResumed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vague {Wave} : {Candidates} bases en retard, {Migrated} migrées, {Failed} en échec, {Skipped} sous bail ailleurs, en {Elapsed}")]
    private static partial void LogWave(ILogger logger, string wave, int candidates, int migrated, int failed, int skipped, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe du Worker en échec")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}
