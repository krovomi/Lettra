using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Security;

namespace Lettra.App.Maui;

/// <summary>Application MAUI : une fenêtre, une BlazorWebView qui héberge l'UI partagée.</summary>
public sealed class LettraApp(DeviceLock deviceLock, AppStartup startup) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState)
    {
        // D'abord l'écran d'ouverture : la BlazorWebView ne s'affiche qu'une fois les données du poste ouvertes.
        Window window = new(new StartupPage(startup)) { Title = "Lettra" };

        // Application en arrière-plan : l'écran se reverrouille (sans effet tant qu'aucun code PIN n'est défini).
        window.Stopped += (_, _) => deviceLock.Lock();
        return window;
    }
}

/// <summary>Page unique : la BlazorWebView avec le routeur partagé.</summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BlazorWebView webView = new() { HostPage = "wwwroot/index.html" };
#if LETTRA_GRID_BENCHMARK
        // Version de mesure (artefact CI) : s'ouvre sur la mesure de la grille et la lance aussitôt.
        webView.StartPath = "/mesure-grille?auto=1";
#endif
        // Micro de la dictée : accordé à la seule page de l'application (MicrophoneAccess, par plateforme).
        webView.BlazorWebViewInitialized += MicrophoneAccess.OnWebViewInitialized;
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        Content = webView;
    }
}
