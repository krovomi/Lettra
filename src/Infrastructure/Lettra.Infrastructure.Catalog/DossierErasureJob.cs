using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

public sealed class DossierErasureJobOptions : IPeriodicJobOptions
{
    public const string SectionName = "Lettra:DossierErasure";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>La passe d'effacement : efface les dossiers dont le délai de rétractation est passé, rend leur nombre.</summary>
internal interface IDossierErasurePass
{
    Task<int> EraseDueAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Passe d'effacement des dossiers (Worker ; l'API s'en charge quand il n'y a pas de Worker) : chaque dossier dont le
/// délai de rétractation est passé perd sa clé, puis sa base. Rien à faire sans dossier en attente d'effacement.
/// </summary>
internal sealed partial class DossierErasureJob(
    IDossierErasurePass eraser,
    IOptions<DossierErasureJobOptions> options,
    TimeProvider clock,
    ILogger<DossierErasureJob> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DossierErasureJobOptions settings = options.Value;
        return settings.Enabled
            ? PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, RunPassAsync, stoppingToken)
            : Task.CompletedTask;
    }

    internal async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            int erased = await eraser.EraseDueAsync(cancellationToken).ConfigureAwait(false);
            if (erased > 0)
            {
                LogPass(erased);
            }
        }
        // Seul l'arrêt du Worker interrompt la boucle : un délai dépassé (TaskCanceledException d'un appel HTTP) est une
        // panne comme une autre, reprise à la passe suivante.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Effacement des dossiers : {Erased} dossiers effacés")]
    private partial void LogPass(int erased);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe d'effacement des dossiers en échec")]
    private partial void LogFailed(Exception exception);
}

public static class DossierErasureJobRegistration
{
    /// <summary>La passe d'effacement des dossiers, sur un hôte qui a le catalogue (<c>AddCatalogDatabase</c>).</summary>
    public static IServiceCollection AddDossierErasureJob(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<DossierErasureJobOptions>().Bind(configuration.GetSection(DossierErasureJobOptions.SectionName))
            .ValidatePeriodicJob(DossierErasureJobOptions.SectionName);
        services.AddHostedService<DossierErasureJob>();
        return services;
    }
}
