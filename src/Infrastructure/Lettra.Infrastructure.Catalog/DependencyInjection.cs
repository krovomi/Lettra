using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Catalog;

public static class DependencyInjection
{
    public static IServiceCollection AddConfigurationCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogOptions>().Bind(configuration.GetSection(CatalogOptions.SectionName));
        services.AddSingleton<ITenantCatalog, ConfigurationTenantCatalog>();
        // La configuration reçue est capturée : la composition ne suppose pas qu'IConfiguration soit dans le conteneur.
        services.AddSingleton<ISecretStore>(new ConfigurationSecretStore(configuration));
        return services;
    }
}
