using Lettra.UI.Shared.Keyboard;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared;

public static class DependencyInjection
{
    /// <summary>Services de l'UI partagée (localisation, registre des raccourcis clavier). Les gateways sont ajoutées par l'hôte selon le mode.</summary>
    public static IServiceCollection AddLettraUi(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddScoped<ShortcutRegistry>();
        return services;
    }
}
