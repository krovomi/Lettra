using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Catalog;

public static class DependencyInjection
{
    /// <summary>
    /// Modes 2, 3 et 5 : base Catalog (provider choisi par la composition root), sources de secrets <c>config:</c> et
    /// <c>cluster:</c>, KEK des cabinets injectées par la cellule, création et migration des bases de dossiers.
    /// </summary>
    public static IServiceCollection AddCatalogDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder> configureCatalog)
    {
        services.AddOptions<CatalogOptions>().Bind(configuration.GetSection(CatalogOptions.SectionName));
        services.AddPooledDbContextFactory<CatalogDbContext>(configureCatalog);

        // La configuration reçue est capturée : la composition ne suppose pas qu'IConfiguration soit dans le conteneur.
        services.AddSingleton<ISecretSource>(new ConfigurationSecretSource(configuration));
        services.AddSingleton<ISecretSource, ClusterSecretSource>();
        services.AddSingleton<ISecretSource, ClusterAdminSecretSource>();
        services.AddSingleton<IKekStore, ConfigurationKekStore>();

        services.AddSingleton<CatalogTenantCatalog>();
        services.AddSingleton<ITenantCatalog>(sp => sp.GetRequiredService<CatalogTenantCatalog>());
        services.AddSingleton<IDossierDirectory>(sp => sp.GetRequiredService<CatalogTenantCatalog>());
        services.AddSingleton<CatalogDossierProvisioner>();
        services.AddSingleton<IDossierProvisioner>(sp => sp.GetRequiredService<CatalogDossierProvisioner>());
        // Avant l'identité serveur, qui n'enregistre son registre lu en configuration qu'à défaut (TryAdd).
        services.AddSingleton<IAuthorizationDirectory, CatalogAuthorizationDirectory>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorizationAdministration, CatalogAuthorizationAdministration>();
        services.AddSingleton<CatalogInitializer>();
        services.AddSingleton<DossierMigrationOrchestrator>();
        return services;
    }

    /// <summary>Migration du catalogue et import de la configuration, au démarrage de l'hôte.</summary>
    public static Task InitializeCatalogAsync(this IServiceProvider services, bool migrate, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogInitializer>().InitializeAsync(migrate, cancellationToken);

    /// <summary>Reprise des créations de dossiers interrompues (Worker).</summary>
    public static Task<int> ResumeDossierProvisioningAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogDossierProvisioner>().ResumeAsync(cancellationToken);
}
