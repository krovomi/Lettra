using Lettra.Application;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.External;
using Lettra.Infrastructure.Identity.Local;
using Lettra.Infrastructure.Identity.Oidc;
using Lettra.Infrastructure.Observability;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.SqlServer;
using Lettra.Infrastructure.Persistence.Sync;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Speech;
using Lettra.Supervision.Infrastructure;
using Lettra.Sync.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Bootstrap;

/// <summary>
/// Composition root. Le mode de déploiement (<c>Lettra:DeploymentMode</c>) choisit les adapters ;
/// le métier n'en sait rien. L'hôte ajoute seulement ce qui dépend de lui : la source du dossier
/// demandé (<see cref="IRequestedDossierSource"/>), le coffre de l'OS sur le poste (<c>IKekStore</c>)
/// et, côté API, l'authentification.
/// </summary>
public static class LettraBootstrap
{
    public const string DeploymentModeKey = "Lettra:DeploymentMode";

    /// <summary>Répertoire des dossiers du poste (modes 1 et 4).</summary>
    public const string LocalDataDirectoryKey = "Lettra:Local:DataDirectory";

    /// <summary>Poste relié (mode 4) : identifiant, sur le serveur, du dossier dont le poste tient la réplique.</summary>
    public const string ReplicaDossierIdKey = "Lettra:Local:ReplicaDossierId";

    public static DeploymentMode GetDeploymentMode(this IConfiguration configuration) =>
        configuration.GetValue<DeploymentMode?>(DeploymentModeKey)
        ?? throw new InvalidOperationException($"{DeploymentModeKey} est obligatoire (1 à 5).");

    public static IServiceCollection AddLettra(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DeploymentMode mode = configuration.GetDeploymentMode();

        // Communs à tous les modes. Chaque chantier remplit son AddXxx ; la composition root ne change pas.
        services.AddApplication();
        // En SaaS, l'API ne migre pas : le Worker migre toutes les bases en amont, par vagues.
        services.AddPersistenceCore(new PersistenceOptions { MigrateOnOpen = mode != DeploymentMode.HostedSaaS });
        services.AddExternalServices(configuration);
        services.AddLettraObservability(configuration, mode);
        services.AddLettraAi(configuration);
        services.AddSpeech(configuration);
        services.AddExports(configuration);

        return mode switch
        {
            DeploymentMode.IsolatedDesktop => services
                .AddLocalDossiers(LocalDataDirectory(configuration, mode))
                .AddLocalIdentity(configuration)
                .AddSupervision(configuration)
                .AddSupervisionDatabase(b => SqliteConfigurator.ConfigureSupervision(
                    b, $"Data Source={Path.Combine(LocalDataDirectory(configuration, mode), "supervision.db")}")),

            // Serveur du cabinet : dossiers sur SQLite, PostgreSQL ou SQL Server, catalogue sur le même serveur.
            DeploymentMode.LocalNetwork => services
                .AddSqliteProvider()
                .AddPostgreSqlProvider()
                .AddSqlServerProvider()
                .AddCatalogDatabase(configuration, CatalogDatabase(configuration))
                .AddOidcIdentity(configuration)
                .AddSupervision(configuration)
                .AddSupervisionDatabase(SupervisionDatabase(configuration))
                .AddSyncServer()
                .AddSyncJournal(),

            // PostgreSQL dédié (mode 3) ou cellule SaaS (mode 5) : une base par dossier, derrière PgBouncer en SaaS.
            DeploymentMode.OnPremRemote or DeploymentMode.HostedSaaS => services
                .AddPostgreSqlProvider()
                .AddCatalogDatabase(configuration, CatalogDatabase(configuration))
                .AddOidcIdentity(configuration)
                .AddSupervision(configuration)
                .AddSupervisionDatabase(SupervisionDatabase(configuration))
                .AddSyncServer()
                .AddSyncJournal(),

            // Poste relié à un serveur (modes 2, 3 et 5 vus du poste) : réplique chiffrée du dossier du serveur, travail
            // hors ligne comme en mode 1, pièces validées dans la série du poste envoyées par la synchronisation
            // (ADR-0008). L'hôte ajoute AddSyncClient, le transport et la sonde réseau.
            DeploymentMode.HybridCloud => services
                .AddLocalDossiers(LocalDataDirectory(configuration, mode))
                .AddLocalIdentity(configuration)
                .AddSupervision(configuration)
                .AddSupervisionDatabase(b => SqliteConfigurator.ConfigureSupervision(
                    b, $"Data Source={Path.Combine(LocalDataDirectory(configuration, mode), "supervision.db")}"))
                .AddSyncDevice(),

            _ => throw new NotSupportedException($"Mode {mode} inconnu."),
        };
    }

    /// <summary>
    /// À appeler au démarrage de l'hôte, après la construction du conteneur. Poste : cabinet local et premier dossier.
    /// Serveur : migration du catalogue (sauf en SaaS, où le Worker s'en charge) et import des déclarations de la configuration.
    /// </summary>
    public static async Task InitializeLettraAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        switch (configuration.GetDeploymentMode())
        {
            case DeploymentMode.IsolatedDesktop:
                await services.GetRequiredService<LocalDossierStartup>().EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
                await services.MigrateSupervisionAsync(cancellationToken).ConfigureAwait(false);
                break;
            case DeploymentMode.HybridCloud:
                await services.GetRequiredService<LocalDossierStartup>()
                    .EnsureReplicaReadyAsync(new DossierId(ReplicaDossierId(configuration)), cancellationToken).ConfigureAwait(false);
                await services.MigrateSupervisionAsync(cancellationToken).ConfigureAwait(false);
                break;
            case DeploymentMode.LocalNetwork or DeploymentMode.OnPremRemote or DeploymentMode.HostedSaaS:
                bool migrate = configuration.GetValue("Lettra:Catalog:MigrateOnStartup", configuration.GetDeploymentMode() != DeploymentMode.HostedSaaS);
                await services.InitializeCatalogAsync(migrate, cancellationToken).ConfigureAwait(false);
                if (migrate)
                {
                    await services.MigrateSupervisionAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }

    private static Guid ReplicaDossierId(IConfiguration configuration) =>
        configuration.GetValue<Guid?>(ReplicaDossierIdKey) is Guid id && id != Guid.Empty
            ? id
            : throw new InvalidOperationException($"{ReplicaDossierIdKey} est obligatoire en mode {DeploymentMode.HybridCloud}.");

    private static string LocalDataDirectory(IConfiguration configuration, DeploymentMode mode) =>
        configuration[LocalDataDirectoryKey] ?? throw new InvalidOperationException($"{LocalDataDirectoryKey} est obligatoire en mode {mode}.");

    /// <summary>
    /// Base de supervision des modes serveur : par défaut celle du catalogue (même provider, même chaîne de connexion),
    /// sous son schéma ; <c>Lettra:Supervision:ConnectionStringName</c> la sépare si l'exploitation le souhaite.
    /// </summary>
    private static Action<DbContextOptionsBuilder> SupervisionDatabase(IConfiguration configuration) => builder =>
    {
        DatabaseProvider provider = configuration.GetValue("Lettra:Catalog:Provider", DatabaseProvider.Sqlite);
        string name = configuration.GetValue<string?>("Lettra:Supervision:ConnectionStringName")
            ?? configuration.GetValue("Lettra:Catalog:ConnectionStringName", "Catalog");
        string connectionString = configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"ConnectionStrings:{name} (base de supervision) est obligatoire.");
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                SqliteConfigurator.ConfigureSupervision(builder, connectionString);
                break;
            case DatabaseProvider.PostgreSql:
                PostgreSqlConfigurator.ConfigureSupervision(builder, connectionString);
                break;
            case DatabaseProvider.SqlServer:
                SqlServerConfigurator.ConfigureSupervision(builder, connectionString);
                break;
            default:
                throw new NotSupportedException($"Provider {provider} inconnu pour la supervision.");
        }
    };

    /// <summary>
    /// Lu à la construction des options, pas à l'enregistrement : la configuration complète (y compris les sources
    /// ajoutées après coup, par exemple par les hôtes de test) est alors connue.
    /// </summary>
    private static Action<DbContextOptionsBuilder> CatalogDatabase(IConfiguration configuration) => builder =>
    {
        DatabaseProvider provider = configuration.GetValue("Lettra:Catalog:Provider", DatabaseProvider.Sqlite);
        string name = configuration.GetValue("Lettra:Catalog:ConnectionStringName", "Catalog");
        string connectionString = configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"ConnectionStrings:{name} (base Catalog) est obligatoire.");
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                SqliteConfigurator.ConfigureCatalog(builder, connectionString);
                break;
            case DatabaseProvider.PostgreSql:
                PostgreSqlConfigurator.ConfigureCatalog(builder, connectionString);
                break;
            case DatabaseProvider.SqlServer:
                SqlServerConfigurator.ConfigureCatalog(builder, connectionString);
                break;
            default:
                throw new NotSupportedException($"Provider {provider} inconnu pour le catalogue.");
        }
    };
}
