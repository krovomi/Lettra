using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Services;

/// <summary>Libération des modules JS à la fermeture d'un composant ou d'un service.</summary>
public static class JsModules
{
    /// <summary>
    /// Appelle les fonctions de nettoyage du module, puis le libère. À la fermeture, un hôte déjà parti
    /// (<see cref="JSDisconnectedException"/>) ou un module déjà détaché (<see cref="JSException"/>) n'est pas une erreur.
    /// </summary>
    public static async ValueTask ReleaseAsync(IJSObjectReference? module, params string[] cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (module is null)
        {
            return;
        }

        try
        {
            foreach (string function in cleanup)
            {
                await module.InvokeVoidAsync(function).ConfigureAwait(false);
            }

            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }
}
