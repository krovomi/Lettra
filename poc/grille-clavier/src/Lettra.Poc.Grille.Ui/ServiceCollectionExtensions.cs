using Lettra.Poc.Grille.Ui.Clavier;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Poc.Grille.Ui;

public static class ServiceCollectionExtensions
{
    /// <summary>Services de saisie clavier, partagés par l'hôte WASM et l'hôte MAUI Hybrid.</summary>
    public static IServiceCollection AddLettraGrilleClavier(this IServiceCollection services) =>
        services.AddScoped<ShortcutRegistry>();
}
