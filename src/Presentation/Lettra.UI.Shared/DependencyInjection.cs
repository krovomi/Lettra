using Lettra.Printing;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.UI.Shared;

public static class DependencyInjection
{
    /// <summary>
    /// Services de l'UI partagée (localisation, registre des raccourcis clavier, préférences, palette de commandes,
    /// rendu PDF local). Les gateways sont ajoutées par l'hôte selon le mode.
    /// </summary>
    public static IServiceCollection AddLettraUi(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddScoped<ShortcutRegistry>();
        services.AddScoped<UiPreferences>();
        // Un hôte peut remplacer le téléchargement (enregistrement natif en MAUI) en l'enregistrant avant.
        services.TryAddScoped<IFileDownloader, JsFileDownloader>();
        services.AddSingleton<IDocumentPdfRenderer, PdfDocumentRenderer>();
        services.AddScoped<ICommandPaletteProvider, NavigationPaletteProvider>();
        services.AddScoped<ICommandPaletteProvider, BillingPaletteProvider>();
        return services;
    }
}
