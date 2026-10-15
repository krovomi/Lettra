using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Security;

namespace Lettra.App.Maui;

/// <summary>Application MAUI : une fenêtre, une BlazorWebView qui héberge l'UI partagée.</summary>
public sealed class LettraApp(DeviceLock deviceLock) : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState)
    {
        Window window = new(new MainPage()) { Title = "Lettra" };

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
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        Content = webView;
    }
}
