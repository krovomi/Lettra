using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Preferences;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Catalog.Kpi;
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
        services.AddOptions<CatalogOptions>().Bind(configuration.GetSection(CatalogOptions.SectionName))
            .Validate(o => o.Catalog.ProvisioningWait > TimeSpan.Zero, $"{CatalogOptions.SectionName}:Catalog:ProvisioningWait doit être strictement positif.")
            .ValidateOnStart();
        services.AddPooledDbContextFactory<CatalogDbContext>(configureCatalog);

        // La configuration reçue est capturée : la composition ne suppose pas qu'IConfiguration soit dans le conteneur.
        services.AddSingleton<ISecretSource>(new ConfigurationSecretSource(configuration));
        services.AddSingleton<CabinetDatabaseRoles>();
        services.AddSingleton<ISecretSource, ClusterSecretSource>();
        services.AddSingleton<ISecretSource, ClusterAdminSecretSource>();
        services.AddCabinetKeys(configuration);

        // Champs sensibles des dossiers chiffrés par leur DEK (remplace l'absence de clé du poste).
        services.AddSingleton<CatalogDossierFieldKeys>();
        services.Replace(ServiceDescriptor.Singleton<IDossierFieldKeys>(sp => sp.GetRequiredService<CatalogDossierFieldKeys>()));

        services.AddSingleton<CatalogTenantCatalog>();
        services.AddSingleton<ITenantCatalog>(sp => sp.GetRequiredService<CatalogTenantCatalog>());
        services.AddSingleton<IDossierDirectory>(sp => sp.GetRequiredService<CatalogTenantCatalog>());
        services.AddSingleton<CabinetDatabaseRoleKeeper>();
        services.AddSingleton<CatalogDossierProvisioner>();
        services.AddSingleton<IDossierProvisioner>(sp => sp.GetRequiredService<CatalogDossierProvisioner>());
        services.AddSingleton<IDossierRequestPass>(sp => sp.GetRequiredService<CatalogDossierProvisioner>());
        // Effacement des dossiers créés par le catalogue (remplace le « aucun » du poste enregistré par AddApplication).
        services.AddSingleton<CatalogDossierEraser>();
        services.Replace(ServiceDescriptor.Singleton<IDossierEraser>(sp => sp.GetRequiredService<CatalogDossierEraser>()));
        services.AddSingleton<CatalogCabinetLifecycle>();
        services.Replace(ServiceDescriptor.Singleton<ICabinetLifecycle>(sp => sp.GetRequiredService<CatalogCabinetLifecycle>()));
        services.AddSingleton<IDossierErasurePass, CatalogErasurePass>();
        // Avant l'identité serveur, qui n'enregistre son registre lu en configuration qu'à défaut (TryAdd).
        services.AddSingleton<CatalogAuthorizationDirectory>();
        services.AddSingleton<IAuthorizationDirectory>(sp => sp.GetRequiredService<CatalogAuthorizationDirectory>());
        services.AddSingleton<IScopeMemberDirectory>(sp => sp.GetRequiredService<CatalogAuthorizationDirectory>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAuthorizationAdministration, CatalogAuthorizationAdministration>();
        services.AddSingleton<IAuthorizationRegistry, CatalogAuthorizationRegistry>();
        // Réglages IA par cabinet (remplace le magasin « indisponible » enregistré par l'application).
        services.Replace(ServiceDescriptor.Singleton<IAiSettingsStore, CatalogAiSettingsStore>());
        // Préférences de chaque utilisateur, qui le suivent sur tous ses appareils (remplace le magasin « indisponible »).
        services.Replace(ServiceDescriptor.Singleton<IUserPreferencesStore, CatalogUserPreferencesStore>());

        // Remplace la politique « aucune » du mode 1 enregistrée par AddApplication.
        services.AddSingleton<IOrganisationPolicyStore, CatalogOrganisationPolicyStore>();
        // Vue cabinet : les indicateurs des dossiers dans le catalogue (remplace le magasin en mémoire du poste).
        services.Replace(ServiceDescriptor.Singleton<IDossierIndicatorStore, CatalogDossierIndicatorStore>());
        // Indicateurs de pilotage pour le Grafana de la cellule (point d'accès exposé seulement avec un jeton).
        services.AddKpiCollection(configuration);
        services.AddSingleton<CatalogInitializer>();
        services.AddSingleton<DossierMigrationOrchestrator>();
        return services;
    }

    /// <summary>
    /// KEK des cabinets : déclarées dans le coffre de la cellule (par défaut), ou tenues par le moteur Transit d'OpenBao
    /// (<c>Lettra:Kms:Provider = Transit</c>), où la clé racine de chaque cabinet ne quitte jamais le KMS.
    /// </summary>
    private static IServiceCollection AddCabinetKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KmsOptions>().Bind(configuration.GetSection(KmsOptions.SectionName))
            .Validate(
                o => o.Provider != KmsProvider.Transit || (o.Transit.Address is not null && !string.IsNullOrWhiteSpace(o.Transit.Token)),
                $"{KmsOptions.SectionName}:Transit:Address et Token sont obligatoires avec le KMS Transit.")
            .Validate(o => o.Transit.CacheLifetime > TimeSpan.Zero, $"{KmsOptions.SectionName}:Transit:CacheLifetime doit être strictement positif.")
            .ValidateOnStart();
        if (configuration.GetValue($"{KmsOptions.SectionName}:{nameof(KmsOptions.Provider)}", KmsProvider.Configuration) != KmsProvider.Transit)
        {
            services.AddSingleton<IKekStore, ConfigurationKekStore>();
            return services;
        }

        services.AddHttpClient(TransitKekStore.HttpClientName);
        services.AddSingleton<TransitKekStore>();
        services.AddSingleton<IKekStore>(sp => sp.GetRequiredService<TransitKekStore>());
        services.AddSingleton<ICabinetKeyVault>(sp => sp.GetRequiredService<TransitKekStore>());
        return services;
    }

    /// <summary>Migration du catalogue et import de la configuration, au démarrage de l'hôte.</summary>
    public static Task InitializeCatalogAsync(this IServiceProvider services, bool migrate, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogInitializer>().InitializeAsync(migrate, cancellationToken);

    /// <summary>Reprise des créations de dossiers interrompues (Worker).</summary>
    public static Task<int> ResumeDossierProvisioningAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogDossierProvisioner>().ResumeAsync(retryFailuresNow: false, cancellationToken);

    /// <summary>Passage des bases d'avant les rôles par cabinet sur le rôle de leur cabinet (Worker).</summary>
    public static Task<int> AdoptCabinetDatabaseRolesAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CabinetDatabaseRoleKeeper>().AdoptAsync(cancellationToken);

    /// <summary>Dossiers actifs de la cellule (passes système du Worker).</summary>
    public static Task<IReadOnlyList<TenantDescriptor>> ListActiveDossiersAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<CatalogTenantCatalog>().ListActiveAsync(cancellationToken);
}
