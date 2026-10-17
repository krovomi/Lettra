using Microsoft.AspNetCore.Components.WebView;

namespace Lettra.App.Maui;

/// <summary>
/// Micro de la dictée dans la BlazorWebView : accordé à la seule page de l'application (origine locale de la
/// BlazorWebView), jamais à une autre origine. Android : autorisation RECORD_AUDIO demandée au premier usage, puis
/// capture audio accordée à la WebView. Windows : WebView2 autorise le micro sans invite pour l'application. iOS et
/// Mac Catalyst : WKWebView demande à l'utilisateur, avec le texte NSMicrophoneUsageDescription (Info.plist).
/// L'audio reste sur l'appareil : il est transcrit par Whisper.net (<c>AddOnDeviceSpeech</c>).
/// </summary>
internal static partial class MicrophoneAccess
{
    /// <summary>À brancher sur <see cref="Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView.BlazorWebViewInitialized"/>.</summary>
    public static void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e) => Allow(e);

    /// <summary>
    /// Origine locale de la BlazorWebView (https://0.0.0.1, ou https://0.0.0.0 avec l'ancien réglage de compatibilité) :
    /// seule page à qui le micro est accordé.
    /// </summary>
    internal static bool IsAppOrigin(string? scheme, string? host) =>
        string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) && host is "0.0.0.1" or "0.0.0.0";

    static partial void Allow(BlazorWebViewInitializedEventArgs e);
}
