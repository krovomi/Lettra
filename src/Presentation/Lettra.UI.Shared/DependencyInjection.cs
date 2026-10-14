using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared;

public static class DependencyInjection
{
    /// <summary>Services de l'UI partagée (localisation). Les gateways sont ajoutées par l'hôte selon le mode.</summary>
    public static IServiceCollection AddLettraUi(this IServiceCollection services)
    {
        services.AddLocalization();
        return services;
    }
}
