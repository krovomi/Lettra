using Android.OS;
using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Maui.ApplicationModel;

namespace Lettra.App.Maui;

internal static partial class MicrophoneAccess
{
    static partial void Allow(BlazorWebViewInitializedEventArgs e)
    {
        // Le client de la BlazorWebView (sélecteur de fichiers, liens externes) est gardé et enveloppé. Il ne se lit
        // qu'à partir d'Android 8.0 (API 26) : avant, il reste tel quel et la WebView refuse le micro.
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && e.WebView.WebChromeClient is { } inner)
        {
            e.WebView.SetWebChromeClient(new MicrophoneWebChromeClient(inner));
        }
    }
}

/// <summary>
/// Accorde la capture audio (et elle seule) à la page de l'application, après l'autorisation Android RECORD_AUDIO
/// demandée à l'utilisateur au premier usage. Tout le reste passe au client d'origine de la BlazorWebView.
/// </summary>
internal sealed class MicrophoneWebChromeClient(WebChromeClient inner) : WebChromeClient
{
    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request is null)
        {
            return;
        }

        string[] resources = request.GetResources() ?? [];
        bool audioOnly = resources.Length > 0 && resources.All(r => r == PermissionRequest.ResourceAudioCapture);
        if (!audioOnly || !MicrophoneAccess.IsAppOrigin(request.Origin?.Scheme, request.Origin?.Host))
        {
            request.Deny();
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                PermissionStatus status = await Permissions.RequestAsync<Permissions.Microphone>();
                if (status == PermissionStatus.Granted)
                {
                    request.Grant(resources);
                }
                else
                {
                    request.Deny();
                }
            }
            catch (Exception exception) when (exception is PermissionException or InvalidOperationException)
            {
                request.Deny();
            }
        });
    }

    public override void OnPermissionRequestCanceled(PermissionRequest? request) => inner.OnPermissionRequestCanceled(request);

    public override bool OnShowFileChooser(Android.Webkit.WebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams) =>
        inner.OnShowFileChooser(webView, filePathCallback, fileChooserParams);

    public override bool OnCreateWindow(Android.Webkit.WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg) =>
        inner.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);

    public override void OnCloseWindow(Android.Webkit.WebView? window) => inner.OnCloseWindow(window);

    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage) => inner.OnConsoleMessage(consoleMessage);

    public override void OnProgressChanged(Android.Webkit.WebView? view, int newProgress) => inner.OnProgressChanged(view, newProgress);

    public override void OnReceivedTitle(Android.Webkit.WebView? view, string? title) => inner.OnReceivedTitle(view, title);
}
