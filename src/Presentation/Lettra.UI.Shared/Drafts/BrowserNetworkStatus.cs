using Lettra.UI.Abstractions;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Drafts;

/// <summary>
/// Réseau du navigateur (<c>navigator.onLine</c>, module <c>lettra-drafts.js</c>). Dans le doute (module indisponible),
/// l'appareil est réputé en ligne : la requête part et son échec éventuel est traité comme avant.
/// </summary>
internal sealed class BrowserNetworkStatus(IJSRuntime js) : INetworkStatus
{
    private IJSObjectReference? _module;

    public async ValueTask<bool> IsOfflineAsync(CancellationToken cancellationToken)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, IndexedDbDraftStore.ModulePath).ConfigureAwait(false);
            return !await _module.InvokeAsync<bool>("isOnline", cancellationToken).ConfigureAwait(false);
        }
        catch (JSException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Pas encore d'interop JavaScript (prérendu, tests) : rien ne dit que l'appareil est hors ligne.
            return false;
        }
    }
}
