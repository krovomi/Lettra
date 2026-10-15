using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Lettra.UI.Shared.Drafts;

[JsonSerializable(typeof(OfflineDraft))]
internal sealed partial class OfflineDraftJson : JsonSerializerContext;

/// <summary>Brouillons de l'appareil dans IndexedDB (module <c>lettra-drafts.js</c>) ; .NET écrit et lit le JSON.</summary>
internal sealed class IndexedDbDraftStore(IJSRuntime js) : IOfflineDraftStore, IAsyncDisposable
{
    public const string ModulePath = "./_content/Lettra.UI.Shared/lettra-drafts.js";

    private IJSObjectReference? _module;

    public async Task<IReadOnlyList<OfflineDraft>> ListAsync(CancellationToken cancellationToken)
    {
        IJSObjectReference module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        string[] rows = await module.InvokeAsync<string[]>("listDrafts", cancellationToken).ConfigureAwait(false);
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
        IJSObjectReference module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await module.InvokeVoidAsync("putDraft", cancellationToken, draft.LocalId.ToString("N"),
            JsonSerializer.Serialize(draft, OfflineDraftJson.Default.OfflineDraft)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid localId, CancellationToken cancellationToken)
    {
        IJSObjectReference module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await module.InvokeVoidAsync("deleteDraft", cancellationToken, localId.ToString("N")).ConfigureAwait(false);
    }

    public async Task<string?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        IJSObjectReference module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        return await module.InvokeAsync<string?>("readCache", cancellationToken, key).ConfigureAwait(false);
    }

    public async Task WriteCacheAsync(string key, string value, CancellationToken cancellationToken)
    {
        IJSObjectReference module = await ModuleAsync(cancellationToken).ConfigureAwait(false);
        await module.InvokeVoidAsync("writeCache", cancellationToken, key, value).ConfigureAwait(false);
    }

    private async Task<IJSObjectReference> ModuleAsync(CancellationToken cancellationToken) =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath).ConfigureAwait(false);

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
        services.TryAddScoped<IOfflineDraftStore, IndexedDbDraftStore>();
        services.TryAddScoped<OfflineDrafts>();
        return services;
    }
}
