using Lettra.Application.Abstractions.Tenancy;
using Lettra.Exports.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Exports.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Module Exports : cas d'usage et lecture du journal des ventes dans la base du dossier (tous les modes).</summary>
    public static IServiceCollection AddExports(this IServiceCollection services)
    {
        services.AddScoped<ISalesJournalReader, EfSalesJournalReader>();
        return services.AddExportsApplication();
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
