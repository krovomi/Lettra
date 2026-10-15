using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Client;

public enum NetworkKind
{
    None = 0,
    Wifi = 1,
    Ethernet = 2,
    Cellular = 3,
}

/// <param name="ServerReachable">Le serveur a répondu à la sonde (un évènement « réseau revenu » ne suffit pas : portail captif d'hôtel).</param>
/// <param name="Kind">Réseau utilisé.</param>
public sealed record NetworkStatus(bool ServerReachable, NetworkKind Kind);

/// <summary>Sonde réelle du serveur.</summary>
public interface INetworkProbe
{
    Task<NetworkStatus> ProbeAsync(CancellationToken cancellationToken);
}

public enum SyncConsent
{
    Now = 1,
    Later = 2,
    ShowDetail = 3,
}

/// <summary>Ce qui partirait, pour la demande à l'utilisateur (« 2 créations, 5 modifications »).</summary>
public sealed record SyncSummary(int Creations, int Changes, long EstimatedBytes)
{
    public bool IsEmpty => Creations + Changes == 0;
}

/// <summary>
/// Dialogue non bloquant et pastille de la barre d'état. Implémenté par l'UI à l'étape 4.3 (fil « Écrans ») ; d'ici là
/// <see cref="AskEveryTimeLater"/> reporte toujours, et seule une préférence automatique fait partir l'outbox.
/// </summary>
public interface ISyncConsentPrompt
{
    Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken cancellationToken);

    Task ShowDetailAsync(IReadOnlyList<SyncOutboxItem> pending, CancellationToken cancellationToken);
}

/// <summary>Sans écran de demande : l'utilisateur n'a pas dit oui, rien ne part.</summary>
public sealed class AskEveryTimeLater : ISyncConsentPrompt
{
    public Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken cancellationToken) =>
        Task.FromResult(SyncConsent.Later);

    public Task ShowDetailAsync(IReadOnlyList<SyncOutboxItem> pending, CancellationToken cancellationToken) => Task.CompletedTask;
}

public enum SyncPreference
{
    /// <summary>Défaut : on demande à chaque retour du réseau.</summary>
    AlwaysAsk = 1,

    /// <summary>Sans demander en Wi-Fi ou Ethernet ; on demande en réseau mobile.</summary>
    AutoOnWifi = 2,

    /// <summary>Jamais en réseau mobile (ni envoi, ni question) ; on demande ailleurs.</summary>
    NeverOnCellular = 3,
}

/// <summary>Préférence de l'utilisateur, réglée à l'étape 4.3.</summary>
public interface ISyncPreferences
{
    SyncPreference Current { get; }
}

/// <summary>Accès au serveur : HTTP (<see cref="HttpSyncTransport"/>), ou direct pour les tests.</summary>
public interface ISyncTransport
{
    Task<PushSyncIntentsResponse> PushAsync(Guid dossierId, PushSyncIntentsRequest request, CancellationToken cancellationToken);

    Task<SyncChangesResponse> PullAsync(Guid dossierId, string? token, int limit, CancellationToken cancellationToken);
}

/// <summary>Le serveur a refusé la synchronisation (session expirée, droits retirés) ; rien n'est perdu sur le poste.</summary>
public sealed class SyncRefusedException : Exception
{
    public SyncRefusedException()
        : this(SyncFailure.Unauthenticated)
    {
    }

    public SyncRefusedException(string message)
        : base(message)
    {
    }

    public SyncRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SyncRefusedException(SyncFailure reason)
        : base($"Synchronisation refusée par le serveur : {reason}.") => Reason = reason;

    public SyncFailure Reason { get; } = SyncFailure.Unauthenticated;
}

public sealed class SyncClientOptions
{
    /// <summary>Identifiant du poste, envoyé avec chaque lot (journal des réponses côté serveur).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Intentions par lot envoyé.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Éléments par page reçue.</summary>
    public int PageSize { get; set; } = 500;
}

public enum SyncState
{
    Offline = 1,
    PendingConsent = 2,
    Pushing = 3,
    Pulling = 4,
    ResolvingConflicts = 5,
    UpToDate = 6,
    Failed = 7,
}

public enum SyncFailure
{
    None = 0,

    /// <summary>Le réseau a lâché en route : ce qui n'a pas été acquitté repartira, avec les mêmes clés.</summary>
    ConnectionLost = 1,

    /// <summary>Session expirée : reconnexion demandée avant toute écriture.</summary>
    Unauthenticated = 2,

    /// <summary>Le serveur refuse ce poste ou ce dossier.</summary>
    Forbidden = 3,

    /// <summary>Le dossier de la réplique ne s'ouvre pas sur le poste.</summary>
    DossierUnavailable = 4,
}

/// <summary>Compte rendu d'un cycle, pour l'UI et le journal du poste.</summary>
public sealed record SyncReport(
    SyncState State,
    SyncFailure Failure,
    SyncSummary Summary,
    int Sent,
    int Conflicts,
    int Rejected,
    int Received,
    int KeptLocal,
    bool Postponed);

/// <summary>
/// Cycle de synchronisation du poste (annexe A §3.7, porté du POC de la PR #4) : sonde, résumé, demande à l'utilisateur
/// (sauf préférence, et sans question quand rien ne part), envoi des intentions par lots dans l'ordre de saisie,
/// réception par pages depuis le jeton, compte rendu. Une coupure à n'importe quelle étape laisse l'outbox intacte pour
/// ce qui n'a pas été acquitté ; la réception reprend à la dernière page appliquée.
/// </summary>
public sealed class SyncCoordinator(
    IServiceScopeFactory scopes,
    INetworkProbe probe,
    ISyncConsentPrompt consent,
    ISyncPreferences preferences,
    ISyncTransport transport,
    SyncClientOptions options,
    TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<SyncState> _history = [];

    public SyncState State { get; private set; } = SyncState.Offline;

    /// <summary>États traversés au dernier cycle (pour l'UI et les tests).</summary>
    public IReadOnlyList<SyncState> History => _history;

    /// <summary>À appeler au retour du réseau, au démarrage, ou quand l'utilisateur demande « synchroniser ».</summary>
    public async Task<SyncReport> RunAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _history.Clear();
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                return await RunInDossierAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncReport> RunInDossierAsync(IServiceProvider services, CancellationToken ct)
    {
        SyncSummary summary = new(0, 0, 0);
        int sent = 0, conflicts = 0, rejected = 0, received = 0, kept = 0;
        SyncReport Report(SyncFailure failure = SyncFailure.None, bool postponed = false) =>
            new(State, failure, summary, sent, conflicts, rejected, received, kept, postponed);

        Result opened = await services.GetRequiredService<ITenantInitializer>().InitializeAsync(ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            Enter(SyncState.Failed);
            return Report(SyncFailure.DossierUnavailable);
        }

        Guid dossier = services.GetRequiredService<ITenantContext>().Current.DossierId.Value;
        ISyncOutbox outbox = services.GetRequiredService<ISyncOutbox>();
        ISyncReplica replica = services.GetRequiredService<ISyncReplica>();
        IReadOnlyList<SyncOutboxItem> pending = await outbox.ListAsync(SyncOutboxStatus.Pending, ct).ConfigureAwait(false);
        summary = Summarize(pending);

        // 1. Sonde réelle.
        NetworkStatus network = await probe.ProbeAsync(ct).ConfigureAwait(false);
        if (!network.ServerReachable)
        {
            Enter(SyncState.Offline);
            return Report();
        }

        // 2. Demande à l'utilisateur, seulement si quelque chose part (confirmé par le PO le 14/10/2026).
        if (!summary.IsEmpty)
        {
            SyncPreference preference = preferences.Current;
            if (preference == SyncPreference.NeverOnCellular && network.Kind == NetworkKind.Cellular)
            {
                Enter(SyncState.PendingConsent);
                return Report(postponed: true);
            }

            bool automatic = preference == SyncPreference.AutoOnWifi && network.Kind is NetworkKind.Wifi or NetworkKind.Ethernet;
            if (!automatic)
            {
                Enter(SyncState.PendingConsent);
                SyncConsent answer;
                while ((answer = await consent.AskAsync(summary, network.Kind, ct).ConfigureAwait(false)) == SyncConsent.ShowDetail)
                {
                    await consent.ShowDetailAsync(pending, ct).ConfigureAwait(false);
                }

                if (answer == SyncConsent.Later)
                {
                    return Report(postponed: true);
                }
            }
        }

        try
        {
            // 3. Envoi, par lots, dans l'ordre de saisie. Acquittement local seulement après la réponse.
            Enter(SyncState.Pushing);
            foreach (SyncOutboxItem[] batch in pending.Chunk(Math.Max(1, options.BatchSize)))
            {
                PushSyncIntentsResponse response = await transport.PushAsync(
                    dossier, new PushSyncIntentsRequest(options.DeviceId, [.. batch.Select(i => i.Intent)]), ct).ConfigureAwait(false);
                await outbox.AcknowledgeAsync(response.Results, clock.GetUtcNow(), ct).ConfigureAwait(false);
                foreach (SyncIntentResult result in response.Results)
                {
                    switch (result.Outcome)
                    {
                        case SyncIntentOutcome.Rejected:
                            rejected++;
                            break;
                        case SyncIntentOutcome.Conflict:
                            conflicts++;
                            break;
                        default:
                            sent++;
                            break;
                    }
                }
            }

            // 4. Réception par pages ; chaque page et son jeton sont appliqués ensemble.
            Enter(SyncState.Pulling);
            bool more = true;
            while (more)
            {
                string? token = await replica.GetTokenAsync(ct).ConfigureAwait(false);
                SyncChangesResponse changes = await transport.PullAsync(dossier, token, options.PageSize, ct).ConfigureAwait(false);
                SyncApplyReport applied = await replica.ApplyAsync(changes, ct).ConfigureAwait(false);
                received += applied.Applied;
                kept += applied.KeptLocal;
                more = changes.HasMore;
            }

            // 5. Conflits : les champs sans conflit sont appliqués, le reste attend l'écran de résolution.
            int open = (await outbox.ListConflictsAsync(ct).ConfigureAwait(false)).Count;
            Enter(open > 0 ? SyncState.ResolvingConflicts : SyncState.UpToDate);
            return Report();
        }
        catch (SyncRefusedException refused)
        {
            Enter(SyncState.Failed);
            return Report(refused.Reason);
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            Enter(SyncState.Failed);
            return Report(SyncFailure.ConnectionLost);
        }
    }

    private static SyncSummary Summarize(IReadOnlyList<SyncOutboxItem> pending) => new(
        Creations: pending.Count(i => i.Intent is CreateCustomerIntent or CreateSupplierIntent or CreateCatalogItemIntent),
        Changes: pending.Count(i => i.Intent is not (CreateCustomerIntent or CreateSupplierIntent or CreateCatalogItemIntent)),
        EstimatedBytes: pending.Sum(i => (long)Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(i.Intent, ContractsJsonContext.Default.SyncIntent))));

    public void Dispose() => _gate.Dispose();

    private void Enter(SyncState state)
    {
        State = state;
        _history.Add(state);
    }
}
