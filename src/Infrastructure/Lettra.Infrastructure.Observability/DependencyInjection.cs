using Lettra.Application.Abstractions.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Observability;

public static class DependencyInjection
{
    /// <summary>
    /// Point d'accroche de l'observabilité, appelé dans tous les modes. Le mode permet de n'exporter
    /// vers le collecteur que là où il existe (cellule SaaS, serveur du cabinet).
    /// </summary>
    public static IServiceCollection AddLettraObservability(this IServiceCollection services, IConfiguration configuration, DeploymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _ = mode;
        return services;
    }
}
