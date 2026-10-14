using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.PostgreSql;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.SqlServer;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Composition;

/// Composition root de la persistance : le seul endroit qui connaît le mode de déploiement.
public static class LettraPersistenceBootstrap
{
    public static IServiceCollection AddLettraPersistence(this IServiceCollection services, IConfiguration config)
    {
        var mode = config.GetValue<DeploymentMode>("Lettra:DeploymentMode");

        services.AddLogging();
        services.AddSingleton<IDossierDbContextFactory, DossierDbContextFactory>();
        services.AddSingleton<TenantMigrator>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        services.AddSingleton<IKekStore>(new FileKekStore(config["Lettra:Keys:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "keys")));
        services.AddSingleton<IDossierKeyring, AesGcmDossierKeyring>();

        return mode switch
        {
            DeploymentMode.IsolatedDesktop or DeploymentMode.HybridCloud => services.AddLocalDossiers(config),
            DeploymentMode.LocalNetwork => services.AddServerDossiers(config, config.GetValue("Lettra:Database:Provider", DatabaseProvider.SqlServer)),
            DeploymentMode.OnPremRemote or DeploymentMode.HostedSaaS => services.AddServerDossiers(config, DatabaseProvider.PostgreSql),
            _ => throw new NotSupportedException($"Mode {mode} non supporté."),
        };
    }

    private static IServiceCollection AddLocalDossiers(this IServiceCollection services, IConfiguration config)
    {
        var store = new LocalDossierStore(config["Lettra:Local:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "dossiers"));
        services.AddSingleton(store);
        services.AddSingleton<ILocalDossierStore>(store);
        services.AddSingleton<ISecretStore, LocalSqliteSecretStore>();
        services.AddSingleton<IDbProviderConfigurator, SqliteConfigurator>();
        services.AddSingleton<IDatabaseAdministrator, SqliteDatabaseAdministrator>();
        services.AddSingleton<LocalDossierProvisioner>();
        services.AddScoped<ITenantResolver, LocalTenantResolver>();
        return services;
    }

    private static IServiceCollection AddServerDossiers(this IServiceCollection services, IConfiguration config, DatabaseProvider provider)
    {
        services.AddSingleton<ISecretStore>(new ClusterSecretStore(config));
        _ = provider switch
        {
            DatabaseProvider.PostgreSql => services
                .AddSingleton<IDbProviderConfigurator, PostgreSqlConfigurator>()
                .AddSingleton<IDatabaseAdministrator, PostgreSqlDatabaseAdministrator>(),
            DatabaseProvider.SqlServer => services
                .AddSingleton<IDbProviderConfigurator, SqlServerConfigurator>()
                .AddSingleton<IDatabaseAdministrator, SqlServerDatabaseAdministrator>(),
            _ => throw new NotSupportedException($"{provider} n'est pas un fournisseur serveur."),
        };

        // Catalogue (plan de contrôle) : PostgreSQL dans ce POC.
        var catalog = config["Lettra:Catalog:ConnectionString"]
                      ?? throw new InvalidOperationException("Lettra:Catalog:ConnectionString manquant.");
        services.AddPooledDbContextFactory<CatalogDbContext>(o => o.UseNpgsql(catalog));
        services.AddSingleton<ITenantCatalog, TenantCatalog>();
        services.AddHybridCache();
        services.AddScoped<ITenantResolver, CatalogTenantResolver>();
        services.AddSingleton<DossierProvisioner>();
        services.AddSingleton<DossierMigrationOrchestrator>();
        return services;
    }

    /// Démarrage d'une cellule ou d'un serveur : applique les migrations du catalogue lui-même.
    public static async Task MigrateCatalogAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
