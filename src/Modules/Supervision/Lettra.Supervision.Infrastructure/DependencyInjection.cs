using Lettra.Supervision.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Supervision.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Point d'accroche du module Supervision (cas d'usage et adapters), appelé par la composition root.</summary>
    public static IServiceCollection AddSupervision(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddSupervisionApplication();
    }
}
