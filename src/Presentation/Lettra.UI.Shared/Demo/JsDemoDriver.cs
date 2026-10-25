using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Demo;

/// <summary>Module JS des gestes de la démo commentée.</summary>
public static class DemoModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-demo.js";
}

/// <summary>
/// Gestes joués dans le navigateur ou le WebView MAUI (<c>lettra-demo.js</c>) : mêmes événements qu'un clavier et une
/// souris, reçus par les écrans comme d'habitude. Un élément attendu plus de <see cref="Timeout"/> fait échouer le geste.
/// Le module, importé à la première démo, vit autant que la session de l'application (un seul par utilisateur).
/// </summary>
public sealed class JsDemoDriver(IJSRuntime js, NavigationManager navigation) : IDemoDriver
{
    /// <summary>Délai d'apparition d'un élément : un écran qui charge ses données, une pièce qui s'enregistre.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private IJSObjectReference? _module;

    private static int TimeoutMs => (int)Timeout.TotalMilliseconds;

    public Task NavigateAsync(string path, CancellationToken cancellationToken)
    {
        navigation.NavigateTo(path);
        return Task.CompletedTask;
    }

    public Task<bool> WaitForAsync(string target, CancellationToken cancellationToken) =>
        CallAsync("waitForElement", cancellationToken, target, TimeoutMs);

    public Task<bool> HighlightAsync(string? target, CancellationToken cancellationToken) =>
        CallAsync("highlight", cancellationToken, target, TimeoutMs);

    public Task<bool> TypeAsync(string target, string text, double speed, CancellationToken cancellationToken) =>
        CallAsync("type", cancellationToken, target, text, speed, TimeoutMs);

    public Task<bool> ClickAsync(string target, double speed, CancellationToken cancellationToken) =>
        CallAsync("click", cancellationToken, target, speed, TimeoutMs);

    public async Task ClearAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("clear");
        }
        catch (JSException)
        {
            // Page déjà quittée.
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task<bool> CallAsync(string function, CancellationToken cancellationToken, params object?[] args)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, DemoModule.Path);
            return await _module.InvokeAsync<bool>(function, cancellationToken, args);
        }
        catch (JSException)
        {
            return false;
        }
    }
}
