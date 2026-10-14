using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Repositories;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence;

public static class DependencyInjection
{
    /// <summary>Persistance commune à tous les modes : contexte de dossier, fabrique, dépôts. Les providers et le catalogue sont ajoutés par la composition root.</summary>
    public static IServiceCollection AddPersistenceCore(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantInitializer, TenantInitializer>();
        services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
        services.AddSingleton<TenantMigrator>();
        services.AddScoped<TenantDbSession>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerReadStore, CustomerReadStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddPartiesAndCatalogStores();
        return services;
    }
}
