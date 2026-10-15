using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Exports.Application;
using Lettra.Exports.Application.EInvoicing.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Exports.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Clé de la plateforme agréée : <c>None</c> (par défaut, rien ne part) ou <c>Simulated</c>.</summary>
    public const string PlatformKey = "Lettra:EInvoicing:Platform";

    /// <summary>
    /// Module Exports : cas d'usage, lecture du journal des ventes et transmissions dans la base du dossier (tous les
    /// modes). Sans plateforme agréée configurée (<see cref="PlatformKey"/>), la transmission échoue en
    /// <c>Platform.NotConfigured</c>.
    /// </summary>
    public static IServiceCollection AddExports(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ISalesJournalReader, EfSalesJournalReader>();
        services.AddScoped<IPlatformTransmissionStore, EfPlatformTransmissionStore>();
        services.AddScoped<IPlatformHistoryReader, EfPlatformHistoryReader>();
        string platform = configuration?[PlatformKey] ?? "None";
        switch (platform)
        {
            case "None":
                services.TryAddSingleton<IAccreditedPlatformClient, UnconfiguredPlatformClient>();
                break;
            case "Simulated":
                services.AddSimulatedAccreditedPlatform();
                break;
            default:
                throw new InvalidOperationException($"{PlatformKey} : « {platform} » inconnu (None ou Simulated).");
        }

        return services.AddExportsApplication();
    }

    /// <summary>La plateforme agréée simulée, en mémoire (démonstration et tests ; données fictives).</summary>
    public static IServiceCollection AddSimulatedAccreditedPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.RemoveAll<IAccreditedPlatformClient>();
        services.TryAddSingleton<SimulatedAccreditedPlatform>();
        services.AddSingleton<IAccreditedPlatformClient>(sp => sp.GetRequiredService<SimulatedAccreditedPlatform>());
        return services;
    }

    /// <summary>
    /// Export FEC en masse par le Worker (<c>Lettra:Worker:FecExport</c>, désactivé par défaut) ; l'hôte fournit la liste
    /// des dossiers actifs.
    /// </summary>
    public static IServiceCollection AddFecBulkExport(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<FecBulkExportOptions>().Bind(configuration.GetSection(FecBulkExportOptions.SectionName));
        services.AddSingleton(new ActiveDossierSource(activeDossiers));
        services.AddSingleton<IExportArchive, FileSystemExportArchive>();
        services.AddSingleton<FecBulkExportJob>();
        services.AddHostedService(sp => sp.GetRequiredService<FecBulkExportJob>());
        return services;
    }
}
