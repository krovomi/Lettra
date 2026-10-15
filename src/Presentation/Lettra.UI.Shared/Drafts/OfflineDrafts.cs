using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Shared.Drafts;

/// <summary>Un brouillon de l'appareil est arrivé sur le serveur : l'éditeur qui l'affiche passe à la pièce du serveur.</summary>
public sealed record OfflineDraftSynced(Guid LocalId, OfflineDraftKind Kind, Guid ServerId);

/// <summary>Bilan d'un envoi des brouillons de l'appareil.</summary>
/// <param name="Sent">Brouillons envoyés et retirés de l'appareil.</param>
/// <param name="Kept">Brouillons gardés : refusés (conflit, contenu altéré) ou modifiés pendant l'envoi.</param>
/// <param name="Offline">Le serveur reste injoignable : l'envoi s'est arrêté.</param>
public sealed record OfflineDraftSyncReport(int Sent, int Kept, bool Offline);

/// <summary>
/// Brouillons hors ligne du navigateur (étape 4.5). Quand l'enregistrement automatique d'un devis ou d'une facture
/// échoue faute de réseau, l'éditeur garde la saisie ici ; elle survit au rechargement et part au retour du réseau.
/// L'empreinte SHA-256 du contenu est revérifiée avant l'envoi (une copie altérée n'est pas envoyée) et comparée après
/// (une saisie reprise pendant l'envoi reste pour le tour suivant). Un conflit de version garde la copie et le signale :
/// l'éditeur le résout comme en ligne.
/// </summary>
public sealed class OfflineDrafts(IOfflineDraftStore store, IServiceProvider services, TimeProvider clock) : IDisposable
{
    public const string ReferenceDataKey = "billing.reference";
    public const string CompanyProfileKey = "billing.company";
    public const string CorruptedCode = "Drafts.Corrupted";

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<Guid, Guid> _synced = [];

    public event EventHandler? Changed;

    public event EventHandler<OfflineDraftSynced>? Synced;

    /// <summary>La pièce du serveur créée par l'envoi d'un brouillon de l'appareil, pendant la session.</summary>
    public Guid? SyncedServerId(Guid localId) => _synced.TryGetValue(localId, out Guid id) ? id : null;

    public static bool IsUnreachable<T>(OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors.Any(e => e.Code == ErrorCodes.Api.Unreachable);
    }

    public static string HashOf(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public Task<IReadOnlyList<OfflineDraft>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    public async Task<OfflineDraft?> FindAsync(OfflineDraftKind kind, Guid? serverId, Guid? localId, CancellationToken cancellationToken)
    {
        IReadOnlyList<OfflineDraft> drafts = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return drafts.FirstOrDefault(d => d.Kind == kind && ((localId is not null && d.LocalId == localId) || (serverId is not null && d.ServerId == serverId)));
    }

    public Task<Guid> KeepQuoteAsync(Guid? localId, QuoteResponse? quote, SaveQuoteRequest request, string? title, CancellationToken cancellationToken) =>
        KeepAsync(OfflineDraftKind.Quote, localId, quote?.Id, quote?.Version, title,
            JsonSerializer.Serialize(request, ContractsJsonContext.Default.SaveQuoteRequest), cancellationToken);

    public Task<Guid> KeepInvoiceAsync(Guid? localId, InvoiceResponse? invoice, SaveInvoiceRequest request, string? title, CancellationToken cancellationToken) =>
        KeepAsync(OfflineDraftKind.Invoice, localId, invoice?.Id, invoice?.Version, title,
            JsonSerializer.Serialize(request, ContractsJsonContext.Default.SaveInvoiceRequest), cancellationToken);

    public static SaveQuoteRequest? QuoteRequestOf(OfflineDraft draft) => Read(draft, ContractsJsonContext.Default.SaveQuoteRequest);

    public static SaveInvoiceRequest? InvoiceRequestOf(OfflineDraft draft) => Read(draft, ContractsJsonContext.Default.SaveInvoiceRequest);

    /// <summary>L'éditeur a enregistré en ligne : la copie de l'appareil n'a plus lieu d'être.</summary>
    public async Task ForgetAsync(Guid localId, CancellationToken cancellationToken)
    {
        await store.RemoveAsync(localId, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Données lues en ligne gardées pour rouvrir un brouillon sans réseau : une réponse reçue remplace le cache, un
    /// serveur injoignable rend la dernière gardée ; tout autre refus ne rend rien.
    /// </summary>
    public async Task<T?> CachedAsync<T>(string key, OperationResult<T> result, JsonTypeInfo<T> type, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Value is { } value)
        {
            await store.WriteCacheAsync(key, JsonSerializer.Serialize(value, type), cancellationToken).ConfigureAwait(false);
            return value;
        }

        if (!IsUnreachable(result) || await store.ReadCacheAsync(key, cancellationToken).ConfigureAwait(false) is not { } cached)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(cached, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Envoie les brouillons de l'appareil, les plus anciens d'abord ; un seul envoi à la fois.</summary>
    public async Task<OfflineDraftSyncReport> SyncAsync(CancellationToken cancellationToken)
    {
        if (!await _sync.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new OfflineDraftSyncReport(0, 0, false);
        }

        int sent = 0, kept = 0;
        bool offline = false;
        try
        {
            foreach (OfflineDraft draft in (await store.ListAsync(cancellationToken).ConfigureAwait(false)).OrderBy(d => d.SavedAt))
            {
                Outcome outcome = await SendAsync(draft, cancellationToken).ConfigureAwait(false);
                if (outcome.Offline)
                {
                    offline = true;
                    break;
                }

                if (outcome.ServerId is Guid serverId)
                {
                    _synced[draft.LocalId] = serverId;
                }

                OfflineDraft? current = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(d => d.LocalId == draft.LocalId);
                if (current is null)
                {
                    continue;   // l'éditeur l'a enregistré en ligne entre-temps
                }

                if (outcome.Error is { } error)
                {
                    await store.SaveAsync(current with { LastError = error }, cancellationToken).ConfigureAwait(false);
                    kept++;
                }
                else if (current.ContentHash == draft.ContentHash)
                {
                    await store.RemoveAsync(draft.LocalId, cancellationToken).ConfigureAwait(false);
                    sent++;
                    Synced?.Invoke(this, new OfflineDraftSynced(draft.LocalId, draft.Kind, outcome.ServerId!.Value));
                }
                else
                {
                    // Saisie reprise pendant l'envoi : elle repartira, sur la pièce et la version que l'envoi a laissées.
                    await store.SaveAsync(current with { ServerId = outcome.ServerId, BaseVersion = outcome.Version, LastError = null }, cancellationToken)
                        .ConfigureAwait(false);
                    kept++;
                }
            }
        }
        finally
        {
            _sync.Release();
        }

        if (sent + kept > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new OfflineDraftSyncReport(sent, kept, offline);
    }

    private async Task<Guid> KeepAsync(
        OfflineDraftKind kind, Guid? localId, Guid? serverId, long? version, string? title, string payload, CancellationToken cancellationToken)
    {
        OfflineDraft? existing = await FindAsync(kind, serverId, localId, cancellationToken).ConfigureAwait(false);
        OfflineDraft draft = new(
            existing?.LocalId ?? localId ?? Guid.NewGuid(),
            kind,
            serverId ?? existing?.ServerId,
            // La version de base reste celle de la première saisie hors ligne : c'est sur elle que la saisie s'appuie.
            existing?.BaseVersion ?? version,
            title ?? existing?.Title,
            payload,
            HashOf(payload),
            clock.GetUtcNow());
        await store.SaveAsync(draft, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return draft.LocalId;
    }

    private async Task<Outcome> SendAsync(OfflineDraft draft, CancellationToken cancellationToken)
    {
        if (HashOf(draft.Payload) != draft.ContentHash)
        {
            return new Outcome(false, CorruptedCode, null, null);
        }

        switch (draft.Kind)
        {
            case OfflineDraftKind.Quote when QuoteRequestOf(draft) is { } request:
            {
                IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
                OperationResult<QuoteResponse> result = draft.ServerId is Guid id
                    ? await quotes.UpdateAsync(id, request, draft.BaseVersion, cancellationToken).ConfigureAwait(false)
                    : await quotes.CreateAsync(request, cancellationToken).ConfigureAwait(false);
                return Of(result, result.Value?.Id, result.Value?.Version);
            }

            case OfflineDraftKind.Invoice when InvoiceRequestOf(draft) is { } request:
            {
                IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
                OperationResult<InvoiceResponse> result = draft.ServerId is Guid id
                    ? await invoices.UpdateAsync(id, request, draft.BaseVersion, cancellationToken).ConfigureAwait(false)
                    : await invoices.CreateAsync(request, cancellationToken).ConfigureAwait(false);
                return Of(result, result.Value?.Id, result.Value?.Version);
            }

            default:
                return new Outcome(false, CorruptedCode, null, null);
        }
    }

    private static Outcome Of<T>(OperationResult<T> result, Guid? id, long? version) =>
        IsUnreachable(result) ? new Outcome(true, null, null, null)
        : result.IsSuccess ? new Outcome(false, null, id, version)
        : new Outcome(false, result.Errors.Count > 0 ? result.Errors[0].Code : CorruptedCode, null, null);

    private static T? Read<T>(OfflineDraft draft, JsonTypeInfo<T> type) where T : class
    {
        ArgumentNullException.ThrowIfNull(draft);
        try
        {
            return JsonSerializer.Deserialize(draft.Payload, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _sync.Dispose();

    private sealed record Outcome(bool Offline, string? Error, Guid? ServerId, long? Version);
}
