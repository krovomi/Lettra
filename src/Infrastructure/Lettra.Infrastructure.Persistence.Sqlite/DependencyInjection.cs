using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.Infrastructure.Persistence.Sqlite.Transfer;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<IOpenedLocalDossier>(registry);
        services.AddSqliteProvider();
        services.AddSingleton<ISecretSource, LocalSecretSource>();
        services.AddSingleton<LocalTenantCatalog>();
        services.AddSingleton<ITenantCatalog>(sp => sp.GetRequiredService<LocalTenantCatalog>());
        services.AddSingleton<IDossierDirectory>(sp => sp.GetRequiredService<LocalTenantCatalog>());
        services.AddSingleton<LocalDossierProvisioner>();
        services.AddSingleton<IDossierProvisioner>(sp => sp.GetRequiredService<LocalDossierProvisioner>());
        services.AddSingleton<ILocalCabinet, LocalRegistryCabinet>();
        services.AddSingleton(sp => new LocalDossierStartup(
            registry, sp.GetRequiredService<LocalDossierProvisioner>(), sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Réversibilité : export et import d'un dossier entier dans un paquet chiffré, sur les hôtes qui créent leurs dossiers
    /// (poste isolé, serveur du cabinet, PostgreSQL dédié, cellule SaaS). Remplace le « aucun » enregistré par l'application.
    /// </summary>
    public static IServiceCollection AddDossierTransfer(this IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IDossierTransfer, DossierTransfer>());
}
