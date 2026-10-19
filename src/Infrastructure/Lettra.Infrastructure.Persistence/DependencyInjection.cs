using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Ai;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Cabinet;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Numbering;
using Lettra.Infrastructure.Persistence.Outbox;
using Lettra.Infrastructure.Persistence.Repositories;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sync;
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
        services.AddScoped<EnvelopedIntegrityKeyring>();
        services.AddScoped<IIntegrityKeyring>(sp => sp.GetRequiredService<EnvelopedIntegrityKeyring>());
        services.AddScoped<IIntegrityJournal, EfIntegrityJournal>();
        services.AddScoped<IIntegrityStore, EfIntegrityStore>();
        services.AddScoped<ISealedArchiveBuilder, EfSealedArchiveBuilder>();
        services.AddPartiesAndCatalogStores();
        services.AddBillingStores();
        services.AddSyncStores();
        services.AddOutboxStores();

        // Vue cabinet : revue de dossier et indicateurs du dossier ; les indicateurs sont rangés en mémoire tant que la
        // base Catalog ne remplace pas ce magasin (modes 2, 3 et 5).
        services.AddScoped<IDossierReviewStore, EfDossierReviewStore>();
        services.AddScoped<IDossierIndicatorSource, OverdueInvoicesIndicatorSource>();
        services.AddScoped<IDossierIndicatorSource, PlatformRejectionsIndicatorSource>();
        services.AddScoped<IDossierIndicatorSource, ReviewPendingIndicatorSource>();
        services.TryAddSingleton<IDossierIndicatorStore, InMemoryDossierIndicatorStore>();
        services.AddSingleton<IDossierIndicatorRefresher, DossierIndicatorRefresher>();

        // Journal IA dans la base du dossier (remplace le journal vide enregistré par l'application).
        services.Replace(ServiceDescriptor.Scoped<IAiAuditLog, DossierAiAuditLog>());
        return services;
    }
}
