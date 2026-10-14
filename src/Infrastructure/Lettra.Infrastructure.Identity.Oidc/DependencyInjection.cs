using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Identity.Oidc;

public static class DependencyInjection
{
    /// <summary>Point d'accroche de l'identité OIDC, appelé par la composition root pour les modes 3 et 5.</summary>
    public static IServiceCollection AddOidcIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }
}
