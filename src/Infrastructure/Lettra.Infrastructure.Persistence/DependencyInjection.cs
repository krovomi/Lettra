using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Ai;
using Lettra.Infrastructure.Persistence.Numbering;
using Lettra.Infrastructure.Persistence.Repositories;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Persistance commune à tous les modes : contexte de dossier, fabriques, migrateur, dépôts, numérotation.
    /// Les providers, les sources de secrets et le catalogue sont ajoutés par la composition root.
    /// </summary>
    public static IServiceCollection AddPersistenceCore(this IServiceCollection services, PersistenceOptions? options = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options ?? new PersistenceOptions());
        services.AddSingleton<ISecretStore, SecretStoreRouter>();
        services.AddSingleton<IDossierDbContextFactory, DossierDbContextFactory>();
        services.AddSingleton<TenantMigrator>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantInitializer, TenantInitializer>();
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        services.AddScoped<TenantDbSession>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerReadStore, CustomerReadStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDocumentNumbering, DocumentNumbering>();
        services.AddSingleton<IDossierKeyring, AesGcmDossierKeyring>();
        services.AddPartiesAndCatalogStores();

        // Journal IA dans la base du dossier (remplace le journal vide enregistré par l'application).
        services.Replace(ServiceDescriptor.Scoped<IAiAuditLog, DossierAiAuditLog>());
        return services;
    }
}
