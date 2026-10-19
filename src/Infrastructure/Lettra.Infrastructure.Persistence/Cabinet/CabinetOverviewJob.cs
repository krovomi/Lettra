using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Persistence.Cabinet;

public sealed class CabinetOverviewJobOptions
{
    public const string SectionName = "Lettra:CabinetOverview";

    public bool Enabled { get; set; } = true;

    /// <summary>Une passe par jour : les compteurs de la vue cabinet sont ceux de la nuit.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Attente avant la première passe, le temps que les migrations de démarrage se terminent.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Passe de la vue cabinet : recalcule les indicateurs de chaque dossier actif et les range dans la base Catalog. Le
/// Worker la fait tourner ; une installation sans Worker (démonstration) la fait tourner dans l'API.
/// </summary>
internal sealed partial class CabinetOverviewJob(
    IServiceProvider services,
    Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers,
    IOptions<CabinetOverviewJobOptions> options,
    TimeProvider clock,
    ILogger<CabinetOverviewJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CabinetOverviewJobOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        await PeriodicPass.RunAsync(settings.InitialDelay, settings.Interval, clock, RunPassAsync, stoppingToken).ConfigureAwait(false);
    }

    internal async Task<IndicatorRefreshOutcome> RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<TenantDescriptor> dossiers = await listDossiers(services, cancellationToken).ConfigureAwait(false);
            IndicatorRefreshOutcome outcome = await services.GetRequiredService<IDossierIndicatorRefresher>()
                .RefreshAsync([.. dossiers.Select(d => d.DossierId)], cancellationToken).ConfigureAwait(false);
            LogPass(outcome.Refreshed, outcome.Failed);
            return outcome;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPassFailed(exception);
            return new IndicatorRefreshOutcome(0, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Vue cabinet : {Refreshed} dossiers recalculés, {Failed} en échec")]
    private partial void LogPass(int refreshed, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Passe de la vue cabinet impossible : catalogue illisible")]
    private partial void LogPassFailed(Exception exception);
}

public static class CabinetOverviewHosting
{
    /// <summary>
    /// Passe de nuit de la vue cabinet. <paramref name="listDossiers"/> donne les dossiers actifs de la cellule (le
    /// catalogue) ; réglages sous <c>Lettra:CabinetOverview</c>.
    /// </summary>
    public static IServiceCollection AddCabinetOverviewJob(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> listDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<CabinetOverviewJobOptions>().Bind(configuration.GetSection(CabinetOverviewJobOptions.SectionName));
        services.AddHostedService(sp => new CabinetOverviewJob(
            sp, listDossiers, sp.GetRequiredService<IOptions<CabinetOverviewJobOptions>>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<CabinetOverviewJob>>()));
        return services;
    }
}
