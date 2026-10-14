using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;
using Lettra.UI.Shared;

namespace Lettra.App.Maui;

/// <summary>Application MAUI : une fenêtre, une BlazorWebView qui héberge l'UI partagée.</summary>
public sealed class LettraApp : Microsoft.Maui.Controls.Application
{
    protected override Window CreateWindow(Microsoft.Maui.IActivationState? activationState) =>
        new(new MainPage()) { Title = "Lettra" };
}

/// <summary>Page unique : la BlazorWebView avec le routeur partagé.</summary>
public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        BlazorWebView webView = new() { HostPage = "wwwroot/index.html" };
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        Content = webView;
    }
}
