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
/// Préférences d'affichage de l'utilisateur, partagées par toutes les pages et retenues sur l'appareil (stockage local
/// du navigateur ou du WebView MAUI) :
/// <list type="bullet">
/// <item>la densité (annexe A §4.1), basculée à chaud par Ctrl+Maj+D : Confort au doigt, Expert à la souris et au clavier ;</item>
/// <item>le thème (vague 5) : système par défaut, clair, sombre, ardoise ou contraste élevé, posé par <c>data-theme</c> ;</item>
/// <item>la barre latérale repliée en rail sur grand écran.</item>
/// </list>
/// </summary>
public sealed class UiPreferences(IJSRuntime js)
{
    private const string DensityKey = "lettra.density";
    private const string ThemeKey = "lettra.theme";
    private const string NavKey = "lettra.nav";

    private bool _loaded;

    public UiDensity Density { get; private set; } = UiDensity.Expert;

    public UiTheme Theme { get; private set; } = UiTheme.System;

    /// <summary>La barre latérale est repliée en rail d'icônes (grand écran seulement).</summary>
    public bool NavCollapsed { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Lit les préférences retenues (une fois par session) ; sans stockage, garde les défauts de la plateforme.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await WithModuleAsync(async module =>
        {
            string? density = await module.InvokeAsync<string?>("readPreference", DensityKey).ConfigureAwait(false);
            string? theme = await module.InvokeAsync<string?>("readPreference", ThemeKey).ConfigureAwait(false);
            string? nav = await module.InvokeAsync<string?>("readPreference", NavKey).ConfigureAwait(false);
            UiDensity loadedDensity = Enum.TryParse(density, out UiDensity parsed)
                ? parsed
                : await module.InvokeAsync<bool>("prefersComfort").ConfigureAwait(false) ? UiDensity.Comfort : UiDensity.Expert;
            UiTheme loadedTheme = Enum.TryParse(theme, out UiTheme chosen) && Enum.IsDefined(chosen) ? chosen : UiTheme.System;
            bool collapsed = nav == "collapsed";
            Set(
                () => Density == loadedDensity && Theme == loadedTheme && NavCollapsed == collapsed,
                () => (Density, Theme, NavCollapsed) = (loadedDensity, loadedTheme, collapsed));
            if (loadedTheme != UiTheme.System)
            {
                await module.InvokeVoidAsync("applyTheme", loadedTheme.DataTheme()).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public Task ToggleDensityAsync() => SetDensityAsync(Density == UiDensity.Expert ? UiDensity.Comfort : UiDensity.Expert);

    public async Task SetDensityAsync(UiDensity density)
    {
        if (!Set(() => Density == density, () => Density = density))
        {
            return;
        }

        await WithModuleAsync(module => module.InvokeVoidAsync("writePreference", DensityKey, density.ToString()).AsTask()).ConfigureAwait(false);
    }

    /// <summary>Change de thème sans recharger la page (variables CSS sous <c>data-theme</c>).</summary>
    public async Task SetThemeAsync(UiTheme theme)
    {
        if (!Set(() => Theme == theme, () => Theme = theme))
        {
            return;
        }

        await WithModuleAsync(async module =>
        {
            await module.InvokeVoidAsync("applyTheme", theme.DataTheme()).ConfigureAwait(false);
            await module.InvokeVoidAsync("writePreference", ThemeKey, theme.ToString()).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task ToggleNavAsync()
    {
        NavCollapsed = !NavCollapsed;
        Changed?.Invoke(this, EventArgs.Empty);
        await WithModuleAsync(module => module.InvokeVoidAsync("writePreference", NavKey, NavCollapsed ? "collapsed" : "expanded").AsTask())
            .ConfigureAwait(false);
    }

    /// <summary>Préférence d'affichage d'un écran (bloc replié…) ; <c>null</c> si rien n'est retenu ou sans stockage.</summary>
    public async Task<string?> ReadAsync(string key)
    {
        string? value = null;
        await WithModuleAsync(async module => value = await module.InvokeAsync<string?>("readPreference", key).ConfigureAwait(false))
            .ConfigureAwait(false);
        return value;
    }

    public Task WriteAsync(string key, string value) =>
        WithModuleAsync(module => module.InvokeVoidAsync("writePreference", key, value).AsTask());

    private bool Set(Func<bool> unchanged, Action apply)
    {
        if (unchanged())
        {
            return false;
        }

        apply();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
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
