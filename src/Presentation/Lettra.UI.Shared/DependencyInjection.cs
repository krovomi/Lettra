using Lettra.Printing;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Onboarding;
using Lettra.UI.Shared.Typing;
using Lettra.UI.Shared.Voice;
using Lettra.UI.Shared.Security;
using Lettra.UI.Shared.Services;
using Lettra.UI.Shared.Sync;
using Lettra.UI.Abstractions;
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
        services.AddScoped<AssistantPanelState>();
        services.AddScoped<ToastService>();
        // Session lue une fois pour la mise en page : la garde des écrans (SessionGate) et la barre de session.
        services.TryAddScoped<SessionState>();
        // Un hôte peut remplacer le téléchargement (enregistrement natif en MAUI) en l'enregistrant avant.
        services.TryAddScoped<IFileDownloader, JsFileDownloader>();
        services.AddSingleton<IDocumentPdfRenderer, PdfDocumentRenderer>();
        services.AddScoped<ICommandPaletteProvider, NavigationPaletteProvider>();
        services.AddScoped<ICommandPaletteProvider, BillingPaletteProvider>();
        // Vague 5, lane F : tutoriels des premières fois, liste de démarrage, aide à la saisie.
        services.AddScoped<DevicePreferences>();
        services.AddScoped<GuidedTours>();
        services.AddScoped<StarterProgress>();
        services.AddScoped<ICommandPaletteProvider, TourPaletteProvider>();
        services.AddScoped<TypingAssist>();
        services.AddScoped<TypingHistory>();
        services.AddScoped<PostalCodes>();
        services.AddScoped<ICommandPaletteProvider, TemplatePaletteProvider>();
        services.AddScoped<VoiceInput>();
        services.AddScoped<VoiceCommands>();
        services.AddScoped<ICommandPaletteProvider, VoicePaletteProvider>();
        // Vague 6 : sélecteur de dossier, vue cabinet et revue de dossier.
        services.AddScoped<Cabinet.CurrentDossier>();
        services.AddScoped<ICommandPaletteProvider, Cabinet.DossierPaletteProvider>();
        // Demande de synchronisation : appelée par le cycle du poste, hors de tout composant. L'hôte qui synchronise
        // ajoute son ISyncCenter et relie ISyncConsentDialog au SyncCoordinator.
        services.TryAddSingleton<SyncConsentDialog>();
        services.TryAddSingleton<ISyncConsentDialog>(sp => sp.GetRequiredService<SyncConsentDialog>());
        // Code PIN du poste : seul l'hôte qui a un coffre (MAUI) ajoute son IDeviceLockStore ; sans lui, pas de verrou.
        services.TryAddSingleton(sp => new DeviceLock(sp.GetService<IDeviceLockStore>(), sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
