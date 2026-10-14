using Lettra.Application;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Identity.Local;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.SqlServer;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Bootstrap;

/// <summary>
/// Composition root. Le mode de déploiement (<c>Lettra:DeploymentMode</c>) choisit les adapters ;
/// le métier n'en sait rien. L'hôte ajoute seulement ce qui dépend de lui : la source du dossier
/// demandé (<see cref="IRequestedDossierSource"/>) et, côté API, l'authentification.
/// </summary>
public static class LettraBootstrap
{
    public const string DeploymentModeKey = "Lettra:DeploymentMode";

    public static DeploymentMode GetDeploymentMode(this IConfiguration configuration) =>
        configuration.GetValue<DeploymentMode?>(DeploymentModeKey)
        ?? throw new InvalidOperationException($"{DeploymentModeKey} est obligatoire (1 à 5).");

    public static IServiceCollection AddLettra(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DeploymentMode mode = configuration.GetDeploymentMode();

        services.AddApplication();
        services.AddPersistenceCore();

        return mode switch
        {
            DeploymentMode.IsolatedDesktop => services
                .AddSingleton<IDbProviderConfigurator, SqliteConfigurator>()
                .AddConfigurationCatalog(configuration)
                .AddLocalIdentity(configuration),

            DeploymentMode.LocalNetwork => services
                .AddSingleton<IDbProviderConfigurator, SqliteConfigurator>()
                .AddSingleton<IDbProviderConfigurator, PostgreSqlConfigurator>()
                .AddSingleton<IDbProviderConfigurator, SqlServerConfigurator>()
                .AddConfigurationCatalog(configuration)
                .AddLocalIdentity(configuration),

            DeploymentMode.OnPremRemote or DeploymentMode.HostedSaaS => throw new NotSupportedException(
                $"Mode {mode} : nécessite la base Catalog et Keycloak (phase 2)."),

            DeploymentMode.HybridCloud => throw new NotSupportedException(
                $"Mode {mode} : nécessite la synchronisation (phase 4)."),

            _ => throw new NotSupportedException($"Mode {mode} inconnu."),
        };
    }
}
