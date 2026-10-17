using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Services;

/// <summary>
/// Petites valeurs retenues sur l'appareil (stockage local du navigateur ou du WebView MAUI) : tutoriels vus, liste de
/// démarrage. Un confort, jamais une donnée de dossier : sans JS (tests, prérendu) ou sans stockage, rien n'est retenu
/// et la lecture rend <c>null</c>.
/// </summary>
public sealed class DevicePreferences(IJSRuntime js)
{
    public async Task<string?> ReadAsync(string key)
    {
        string? value = null;
        await WithModuleAsync(async module => value = await module.InvokeAsync<string?>("readPreference", key).ConfigureAwait(false)).ConfigureAwait(false);
        return value;
    }

    public Task WriteAsync(string key, string value) =>
        WithModuleAsync(module => module.InvokeVoidAsync("writePreference", key, value).AsTask());

    private async Task WithModuleAsync(Func<IJSObjectReference, Task> action)
    {
        try
        {
            IJSObjectReference? module = await js.InvokeAsync<IJSObjectReference?>("import", UiModule.Path).ConfigureAwait(false);
            if (module is null)
            {
                return;
            }

            await using (module.ConfigureAwait(false))
            {
                await action(module).ConfigureAwait(false);
            }
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
