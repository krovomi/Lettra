using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Drafts;

[JsonSerializable(typeof(OfflineDraft))]
internal sealed partial class OfflineDraftJson : JsonSerializerContext;

/// <summary>
/// Périmètre des brouillons de l'appareil : l'utilisateur connecté (clé opaque donnée par le BFF) et le dossier ouvert.
/// En ligne, la session le donne et l'onglet le retient ; sans réseau, seul l'onglet qui l'a retenu le retrouve (un
/// nouvel onglet, ou la personne suivante sur le poste, n'a pas de périmètre et ne voit aucun brouillon).
/// </summary>
internal sealed class OfflineDraftScope(IServiceProvider services)
{
    private string? _scope;

    public async Task<string?> CurrentAsync(IJSObjectReference module, CancellationToken cancellationToken)
    {
        if (_scope is not null)
        {
            return _scope;
        }

        if (services.GetService<IDossierSelection>()?.CurrentDossierId is not Guid dossier
            || services.GetService<ISessionGateway>() is not { } sessions)
        {
            return null;
        }

        OperationResult<SessionResponse> session = await sessions.GetAsync(cancellationToken).ConfigureAwait(false);
        if (session.Value is { } current)
        {
            if (!current.IsAuthenticated || current.DeviceStorageKey is not { Length: > 0 } user)
            {
                return null;
            }

            _scope = $"{user}.{dossier:N}";
            await module.InvokeVoidAsync("rememberScope", cancellationToken, _scope).ConfigureAwait(false);
            return _scope;
        }

        if (!OfflineDrafts.IsUnreachable(session))
        {
            return null;
        }

        string? remembered = await module.InvokeAsync<string?>("rememberedScope", cancellationToken).ConfigureAwait(false);
        return remembered is not null && remembered.EndsWith($".{dossier:N}", StringComparison.Ordinal) ? _scope = remembered : null;
    }

    public void Forget() => _scope = null;
}

/// <summary>Brouillons de l'appareil dans IndexedDB (module <c>lettra-drafts.js</c>), une base par périmètre ; .NET écrit et lit le JSON.</summary>
internal sealed class IndexedDbDraftStore(IJSRuntime js, OfflineDraftScope scope) : IOfflineDraftStore, IAsyncDisposable
{
    public const string ModulePath = "./_content/Lettra.UI.Shared/lettra-drafts.js";

    private IJSObjectReference? _module;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        await ScopeAsync(cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<OfflineDraft>> ListAsync(CancellationToken cancellationToken)
    {
        if (await ScopeAsync(cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return [];
        }

        string[] rows = await _module!.InvokeAsync<string[]>("listDrafts", cancellationToken, current).ConfigureAwait(false);
        List<OfflineDraft> drafts = [];
        foreach (string row in rows)
        {
            try
            {
                if (JsonSerializer.Deserialize(row, OfflineDraftJson.Default.OfflineDraft) is { } draft)
                {
                    drafts.Add(draft);
                }
            }
            catch (JsonException)
            {
                // Ligne illisible : ignorée, elle ne peut pas être envoyée.
            }
        }

        return drafts;
    }

    public async Task SaveAsync(OfflineDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string current = await RequiredScopeAsync(cancellationToken).ConfigureAwait(false);
        await _module!.InvokeVoidAsync("putDraft", cancellationToken, draft.LocalId.ToString("N"),
            JsonSerializer.Serialize(draft, OfflineDraftJson.Default.OfflineDraft), current).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid localId, CancellationToken cancellationToken)
    {
        if (await ScopeAsync(cancellationToken).ConfigureAwait(false) is { } current)
        {
            await _module!.InvokeVoidAsync("deleteDraft", cancellationToken, localId.ToString("N"), current).ConfigureAwait(false);
        }
    }

    public async Task<string?> ReadCacheAsync(string key, CancellationToken cancellationToken) =>
        await ScopeAsync(cancellationToken).ConfigureAwait(false) is { } current
            ? await _module!.InvokeAsync<string?>("readCache", cancellationToken, key, current).ConfigureAwait(false)
            : null;

    public async Task WriteCacheAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (await ScopeAsync(cancellationToken).ConfigureAwait(false) is { } current)
        {
            await _module!.InvokeVoidAsync("writeCache", cancellationToken, key, value, current).ConfigureAwait(false);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (await ScopeAsync(cancellationToken).ConfigureAwait(false) is { } current)
        {
            await _module!.InvokeVoidAsync("clearScope", cancellationToken, current).ConfigureAwait(false);
        }
        else
        {
            await _module!.InvokeVoidAsync("forgetScope", cancellationToken).ConfigureAwait(false);
        }

        scope.Forget();
    }

    private async Task<string?> ScopeAsync(CancellationToken cancellationToken)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath).ConfigureAwait(false);
        return await scope.CurrentAsync(_module, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RequiredScopeAsync(CancellationToken cancellationToken) =>
        await ScopeAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Brouillons hors ligne indisponibles : utilisateur ou dossier inconnu.");

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
                // Page fermée.
            }
        }
    }
}

public static class OfflineDraftsRegistration
{
    /// <summary>
    /// Brouillons hors ligne dans le navigateur (étape 4.5), pour l'hôte WASM : sans cet appel (MAUI, poste isolé), les
    /// éditeurs gardent leur comportement en ligne.
    /// </summary>
    public static IServiceCollection AddOfflineDrafts(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<OfflineDraftScope>();
        services.TryAddScoped<IOfflineDraftStore, IndexedDbDraftStore>();
        services.TryAddScoped<OfflineDrafts>();
        return services;
    }
}
