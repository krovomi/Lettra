using Lettra.UI.Shared.Keyboard;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Services;

/// <summary>Module JS des services navigateur (téléchargement, préférences locales).</summary>
public static class UiModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-ui.js";
}

/// <summary>
/// Remet un fichier à l'utilisateur (PDF d'un devis ou d'une facture). Navigateur et MAUI Hybrid : téléchargement
/// par le WebView ; un hôte peut le remplacer (enregistrement natif, partage).
/// </summary>
public interface IFileDownloader
{
    Task DownloadAsync(string fileName, string mimeType, byte[] content, CancellationToken cancellationToken);
}

public sealed class JsFileDownloader(IJSRuntime js) : IFileDownloader, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task DownloadAsync(string fileName, string mimeType, byte[] content, CancellationToken cancellationToken)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, UiModule.Path).ConfigureAwait(false);
        if (_module is null)
        {
            return;   // pas de JS (tests)
        }

        await _module.InvokeVoidAsync("downloadFile", cancellationToken, fileName, mimeType, content).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Hôte fermé.
            }
        }
    }
}

/// <summary>
/// Préférences d'affichage de l'utilisateur, partagées par toutes les pages (annexe A §4.1) : la densité, basculée à
/// chaud par Ctrl+Maj+D et retenue sur le poste. Par défaut : Confort au doigt, Expert à la souris et au clavier.
/// </summary>
public sealed class UiPreferences(IJSRuntime js)
{
    private const string DensityKey = "lettra.density";

    private bool _loaded;

    public UiDensity Density { get; private set; } = UiDensity.Expert;

    public event EventHandler? Changed;

    /// <summary>Lit la préférence retenue (une fois par session) ; sans stockage, garde le défaut de la plateforme.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await WithModuleAsync(async module =>
        {
            string? stored = await module.InvokeAsync<string?>("readPreference", DensityKey).ConfigureAwait(false);
            Set(Enum.TryParse(stored, out UiDensity parsed)
                ? parsed
                : await module.InvokeAsync<bool>("prefersComfort").ConfigureAwait(false) ? UiDensity.Comfort : UiDensity.Expert);
        }).ConfigureAwait(false);
    }

    public Task ToggleDensityAsync() => SetDensityAsync(Density == UiDensity.Expert ? UiDensity.Comfort : UiDensity.Expert);

    public async Task SetDensityAsync(UiDensity density)
    {
        Set(density);
        await WithModuleAsync(module => module.InvokeVoidAsync("writePreference", DensityKey, density.ToString()).AsTask()).ConfigureAwait(false);
    }

    private void Set(UiDensity density)
    {
        if (density == Density)
        {
            return;
        }

        Density = density;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Le stockage local est un confort : sans JS (tests, prérendu) ou sans stockage, la préférence vaut pour la session.</summary>
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
