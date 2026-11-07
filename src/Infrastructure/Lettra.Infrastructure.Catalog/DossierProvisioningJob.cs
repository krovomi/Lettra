using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

public sealed class DossierProvisioningJobOptions : IPeriodicJobOptions
{
    public const string SectionName = "Lettra:DossierProvisioning";

    /// <summary>Court : l'API attend la base qu'elle a demandée (<see cref="CatalogDatabaseOptions.ProvisioningWait"/>).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan InitialDelay { get; set; } = TimeSpan.Zero;
}

/// <summary>La passe des demandes de création et de retrait des bases, rend le nombre de demandes menées.</summary>
internal interface IDossierRequestPass
{
    Task<int> RunRequestsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Passe rapide du Worker d'une cellule où il administre seul les bases des dossiers
/// (<see cref="DatabaseAdministration.Worker"/>) : il crée les bases demandées par l'API et supprime celles des imports
/// manqués. L'API n'a ainsi ni le droit de créer une base, ni celui d'en supprimer une.
/// </summary>
internal sealed partial class DossierProvisioningJob(
    IDossierRequestPass requests,
    IOptions<DossierProvisioningJobOptions> options,
    TimeProvider clock,
    ILogger<DossierProvisioningJob> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        PeriodicPass.RunAsync(options.Value.InitialDelay, options.Value.Interval, clock, RunPassAsync, stoppingToken);

    internal async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            int done = await requests.RunRequestsAsync(cancellationToken).ConfigureAwait(false);
            if (done > 0)
            {
                LogPass(done);
            }
        }
        // Seul l'arrêt du Worker interrompt la boucle : un catalogue injoignable est repris à la passe suivante.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bases des dossiers : {Done} demandes menées")]
    private partial void LogPass(int done);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe des demandes de bases en échec")]
    private partial void LogFailed(Exception exception);
}

public static class DossierProvisioningJobRegistration
{
    /// <summary>
    /// La passe des demandes de bases, sur le Worker d'une cellule qui lui laisse l'administration des bases
    /// (<c>Lettra:Catalog:DatabaseAdministration = Worker</c>) ; sans effet ailleurs, où l'hôte crée lui-même ses bases.
    /// </summary>
    public static IServiceCollection AddDossierProvisioningJob(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string mode = $"{CatalogOptions.SectionName}:{nameof(CatalogOptions.Catalog)}:{nameof(CatalogDatabaseOptions.DatabaseAdministration)}";
        if (configuration.GetValue(mode, DatabaseAdministration.Host) != DatabaseAdministration.Worker)
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<DossierProvisioningJobOptions>().Bind(configuration.GetSection(DossierProvisioningJobOptions.SectionName))
            .ValidatePeriodicJob(DossierProvisioningJobOptions.SectionName);
        services.AddHostedService<DossierProvisioningJob>();
        return services;
    }
}
