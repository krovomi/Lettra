using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.External;

public static class DependencyInjection
{
    /// <summary>Point d'accroche des services externes (SIRENE, VIES), appelé dans tous les modes.</summary>
    public static IServiceCollection AddExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }
}
