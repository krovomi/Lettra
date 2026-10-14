using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.Sqlite;

public static class DependencyInjection
{
    /// <summary>Le provider SQLite seul (bases de dossiers déclarées ou créées sur un serveur SQLite).</summary>
    public static IServiceCollection AddSqliteProvider(this IServiceCollection services) =>
        services
            .AddSingleton<IDbProviderConfigurator, SqliteConfigurator>()
            .AddSingleton<IDatabaseAdministrator, SqliteDatabaseAdministrator>();

    /// <summary>
    /// Modes 1 et 4 : registre local des dossiers dans <paramref name="dataDirectory"/>, bases SQLite chiffrées par
    /// leur DEK. L'hôte fournit <c>IKekStore</c> (coffre de l'OS).
    /// </summary>
    public static IServiceCollection AddLocalDossiers(this IServiceCollection services, string dataDirectory)
    {
        LocalDossierRegistry registry = new(dataDirectory);
        services.AddSingleton(registry);
        services.AddSqliteProvider();
        services.AddSingleton<ISecretSource, LocalSecretSource>();
        services.AddSingleton<LocalTenantCatalog>();
        services.AddSingleton<ITenantCatalog>(sp => sp.GetRequiredService<LocalTenantCatalog>());
        services.AddSingleton<IDossierDirectory>(sp => sp.GetRequiredService<LocalTenantCatalog>());
        services.AddSingleton<IDossierProvisioner, LocalDossierProvisioner>();
        return services;
    }
}
