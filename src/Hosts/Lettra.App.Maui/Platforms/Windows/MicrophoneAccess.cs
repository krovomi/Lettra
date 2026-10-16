using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;

namespace Lettra.App.Maui;

internal static partial class MicrophoneAccess
{
    static partial void Allow(BlazorWebViewInitializedEventArgs e)
    {
        // WebView2 : micro accordé sans invite à la page de l'application, et à elle seule. Windows garde le dernier
        // mot (Paramètres, Confidentialité, Microphone, « Autoriser les applications de bureau à accéder au micro »).
        e.WebView.CoreWebView2.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.Microphone
                && Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? origin)
                && IsAppOrigin(origin.Scheme, origin.Host))
            {
                args.State = CoreWebView2PermissionState.Allow;
            }
        };
    }
}
