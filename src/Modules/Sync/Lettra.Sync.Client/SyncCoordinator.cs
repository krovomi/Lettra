using System.Globalization;
using System.Text;
using System.Text.Json;
using Lettra.Application.Abstractions.Persistence;
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

/// <summary>Ce qui partirait, pour la demande à l'utilisateur (« 2 factures validées, 2 créations, 5 modifications »).</summary>
public sealed record SyncSummary(int Creations, int Changes, long EstimatedBytes, int Validations = 0)
{
    public bool IsEmpty => Creations + Changes + Validations == 0;
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

/// <summary>Préférence de l'utilisateur, réglée depuis l'écran des réglages et gardée sur le poste.</summary>
public interface ISyncPreferences
{
    Task<SyncPreference> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(SyncPreference preference, CancellationToken cancellationToken);
}

/// <summary>Résolution d'un champ disputé, depuis l'écran des conflits.</summary>
public enum SyncResolution
{
    /// <summary>Garder la saisie du poste : elle repart comme une nouvelle modification, sur la version du cabinet.</summary>
    KeepMine = 1,

    /// <summary>Prendre la valeur du cabinet, déjà gardée par le serveur : rien ne repart.</summary>
    TakeServer = 2,
}

/// <summary>Accès au serveur : HTTP (<see cref="HttpSyncTransport"/>), ou direct pour les tests.</summary>
public interface ISyncTransport
{
    Task<PushSyncIntentsResponse> PushAsync(Guid dossierId, PushSyncIntentsRequest request, CancellationToken cancellationToken);

    Task<SyncChangesResponse> PullAsync(Guid dossierId, string? token, int limit, CancellationToken cancellationToken);

    /// <summary>Ouvre ou rend les séries de numérotation du poste (refusé à qui ne valide pas de factures).</summary>
    Task<OpenDeviceSeriesResponse> OpenDeviceSeriesAsync(Guid dossierId, OpenDeviceSeriesRequest request, CancellationToken cancellationToken);
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

/// <summary>
/// Rappel croissant quand des factures validées sur le poste attendent d'être envoyées (ADR-0008) : elles ne partent à
/// la plateforme agréée qu'après la synchronisation.
/// </summary>
public enum SyncReminder
{
    None = 0,

    /// <summary>Une facture attend depuis plus de 24 h.</summary>
    Day = 1,

    /// <summary>Une facture attend depuis plus de 72 h.</summary>
    ThreeDays = 2,
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

    /// <summary>Le cabinet a révoqué ce poste : sa réplique est effacée, il ne se synchronise plus (étape 4.4).</summary>
    DeviceRevoked = 5,
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

    /// <summary>Compte rendu du dernier cycle terminé, <c>null</c> avant le premier.</summary>
    public SyncReport? LastReport { get; private set; }

    /// <summary>L'état a changé, ou un cycle s'est terminé (pastille de la barre, écrans de synchronisation).</summary>
    public event EventHandler? Changed;

    /// <summary>Quand l'utilisateur demande « synchroniser », ou au démarrage : attend un cycle en cours, puis le sien.</summary>
    public async Task<SyncReport> RunAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await RunHeldAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Au retour du réseau (évènement du système) : lance un cycle, sauf si un cycle tourne déjà, qui le couvre. La sonde
    /// du cycle vérifie que le serveur répond vraiment.
    /// </summary>
    public async Task<SyncReport?> NetworkAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await RunHeldAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Résout un champ disputé (écran des conflits). <see cref="SyncResolution.KeepMine"/> remet la saisie du poste par
    /// la commande de modification habituelle : elle part au cycle suivant, sur la version du cabinet reçue entre-temps.
    /// </summary>
    public async Task<Result> ResolveConflictAsync(long conflictId, SyncResolution resolution, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                Result opened = await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false);
                Result resolved = opened.IsFailure
                    ? opened
                    : await SyncConflictResolver.ResolveAsync(scope.ServiceProvider, conflictId, resolution, cancellationToken).ConfigureAwait(false);
                if (resolved.IsSuccess && State == SyncState.ResolvingConflicts
                    && (await scope.ServiceProvider.GetRequiredService<ISyncOutbox>().ListConflictsAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
                {
                    // Plus rien à trancher : la saisie gardée attend le prochain envoi.
                    Enter(SyncState.UpToDate);
                }

                return resolved;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncReport> RunHeldAsync(CancellationToken cancellationToken)
    {
        try
        {
            _history.Clear();
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                SyncReport report = await RunInDossierAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                LastReport = report;
                return report;
            }
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<SyncReport> RunInDossierAsync(IServiceProvider services, CancellationToken ct)
    {
        SyncTally tally = new();
        Result opened = await services.GetRequiredService<ITenantInitializer>().InitializeAsync(ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            Enter(SyncState.Failed);
            return tally.Report(State, SyncFailure.DossierUnavailable);
        }

        SyncCycle cycle = new(
            services.GetRequiredService<ITenantContext>().Current.DossierId.Value,
            services.GetRequiredService<ISyncOutbox>(),
            services.GetRequiredService<ISyncReplica>(),
            tally);
        if (await cycle.Replica.GetSettingAsync(DeviceSettings.RevokedKey, ct).ConfigureAwait(false) is not null)
        {
            Enter(SyncState.Failed);
            return tally.Report(State, SyncFailure.DeviceRevoked);
        }

        IReadOnlyList<SyncOutboxItem> pending = await cycle.Outbox.ListAsync(SyncOutboxStatus.Pending, ct).ConfigureAwait(false);
        tally.Summary = Summarize(pending);

        // 1. Sonde réelle.
        NetworkStatus network = await probe.ProbeAsync(ct).ConfigureAwait(false);
        if (!network.ServerReachable)
        {
            Enter(SyncState.Offline);
            return tally.Report(State);
        }

        // 2. Demande à l'utilisateur, seulement si quelque chose part (confirmé par le PO le 14/10/2026).
        if (!tally.Summary.IsEmpty && !await ConsentAsync(tally.Summary, pending, network, ct).ConfigureAwait(false))
        {
            return tally.Report(State, postponed: true);
        }

        return await ExchangeAsync(cycle, pending, ct).ConfigureAwait(false);
    }

    /// <summary>Vrai si l'envoi part maintenant : selon la préférence de l'utilisateur et le réseau, ou sa réponse.</summary>
    private async Task<bool> ConsentAsync(SyncSummary summary, IReadOnlyList<SyncOutboxItem> pending, NetworkStatus network, CancellationToken ct)
    {
        SyncPreference preference = await preferences.GetAsync(ct).ConfigureAwait(false);
        if (preference == SyncPreference.NeverOnCellular && network.Kind == NetworkKind.Cellular)
        {
            Enter(SyncState.PendingConsent);
            return false;
        }

        bool automatic = preference == SyncPreference.AutoOnWifi && network.Kind is NetworkKind.Wifi or NetworkKind.Ethernet;
        if (automatic)
        {
            return true;
        }

        Enter(SyncState.PendingConsent);
        SyncConsent answer;
        while ((answer = await consent.AskAsync(summary, network.Kind, ct).ConfigureAwait(false)) == SyncConsent.ShowDetail)
        {
            await consent.ShowDetailAsync(pending, ct).ConfigureAwait(false);
        }

        return answer != SyncConsent.Later;
    }

    /// <summary>Les étapes en ligne : envoi, réception, séries débloquées, série du poste, conflits.</summary>
    private async Task<SyncReport> ExchangeAsync(SyncCycle cycle, IReadOnlyList<SyncOutboxItem> pending, CancellationToken ct)
    {
        try
        {
            // 3. Envoi, par lots, dans l'ordre de saisie. Acquittement local seulement après la réponse.
            Enter(SyncState.Pushing);
            await PushAsync(cycle, pending, ct).ConfigureAwait(false);

            // 4. Réception par pages ; chaque page et son jeton sont appliqués ensemble.
            Enter(SyncState.Pulling);
            (SyncPolicyResponse? policy, IReadOnlyList<string> released) = await PullAsync(cycle, ct).ConfigureAwait(false);

            // Contact réussi : le compte de la durée hors ligne repart d'ici, avec la règle du cabinet reçue (étape 4.4).
            await RecordContactAsync(cycle.Replica, policy, ct).ConfigureAwait(false);

            // Série débloquée par le cabinet (étape 4.4) : les pièces refusées repartent tout de suite, puis celles qui les attendaient.
            if (await cycle.Outbox.RequeueAsync(released, ct).ConfigureAwait(false) > 0)
            {
                Enter(SyncState.Pushing);
                await PushAsync(cycle, await cycle.Outbox.ListAsync(SyncOutboxStatus.Pending, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            }

            // 5. Série du poste (ADR-0007), au premier passage en ligne : elle permet ensuite de valider hors ligne.
            await EnsureDeviceSeriesAsync(cycle.Dossier, cycle.Replica, ct).ConfigureAwait(false);

            // 6. Conflits : les champs sans conflit sont appliqués, le reste attend l'écran de résolution.
            int open = (await cycle.Outbox.ListConflictsAsync(ct).ConfigureAwait(false)).Count;
            Enter(open > 0 ? SyncState.ResolvingConflicts : SyncState.UpToDate);
            return cycle.Tally.Report(State);
        }
        catch (SyncRefusedException refused) when (refused.Reason == SyncFailure.DeviceRevoked)
        {
            // Le cabinet a retiré ce poste : rien de ce qui reste ici ne doit rester lisible, outbox comprise.
            await cycle.Replica.WipeAsync(ct).ConfigureAwait(false);
            await cycle.Replica.SaveSettingAsync(DeviceSettings.RevokedKey, Millis(clock.GetUtcNow()), ct).ConfigureAwait(false);
            Enter(SyncState.Failed);
            return cycle.Tally.Report(State, SyncFailure.DeviceRevoked);
        }
        catch (SyncRefusedException refused)
        {
            Enter(SyncState.Failed);
            return cycle.Tally.Report(State, refused.Reason);
        }
        catch (Exception e) when (e is HttpRequestException or IOException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            Enter(SyncState.Failed);
            return cycle.Tally.Report(State, SyncFailure.ConnectionLost);
        }
    }

    private async Task PushAsync(SyncCycle cycle, IReadOnlyList<SyncOutboxItem> items, CancellationToken ct)
    {
        foreach (SyncOutboxItem[] batch in items.Chunk(Math.Max(1, options.BatchSize)))
        {
            PushSyncIntentsResponse response = await transport.PushAsync(
                cycle.Dossier, new PushSyncIntentsRequest(options.DeviceId, [.. batch.Select(i => i.Intent)]), ct).ConfigureAwait(false);
            await cycle.Outbox.AcknowledgeAsync(response.Results, clock.GetUtcNow(), ct).ConfigureAwait(false);
            foreach (SyncIntentResult result in response.Results)
            {
                cycle.Tally.Count(result);
            }
        }
    }

    /// <summary>Toutes les pages ; rend la règle du cabinet et les séries débloquées portées par la dernière.</summary>
    private async Task<(SyncPolicyResponse? Policy, IReadOnlyList<string> Released)> PullAsync(SyncCycle cycle, CancellationToken ct)
    {
        bool more = true;
        SyncPolicyResponse? policy = null;
        IReadOnlyList<string> released = [];
        while (more)
        {
            string? token = await cycle.Replica.GetTokenAsync(ct).ConfigureAwait(false);
            SyncChangesResponse changes = await transport.PullAsync(cycle.Dossier, token, options.PageSize, ct).ConfigureAwait(false);
            cycle.Tally.Add(await cycle.Replica.ApplyAsync(changes, ct).ConfigureAwait(false));
            more = changes.HasMore;
            policy = changes.Policy ?? policy;
            released = changes.Released ?? released;
        }

        return (policy, released);
    }

    /// <summary>Le dossier d'un cycle, ses magasins et ses compteurs.</summary>
    private sealed record SyncCycle(Guid Dossier, ISyncOutbox Outbox, ISyncReplica Replica, SyncTally Tally);

    /// <summary>Ce que compte un cycle, rendu dans son <see cref="SyncReport"/>.</summary>
    private sealed class SyncTally
    {
        private int _sent, _conflicts, _rejected, _received, _kept;

        public SyncSummary Summary { get; set; } = new(0, 0, 0);

        public void Count(SyncIntentResult result)
        {
            switch (result.Outcome)
            {
                // Une pièce qui attend la précédente de sa série reste en attente, sans être comptée refusée.
                case SyncIntentOutcome.Rejected when result.IsWaiting():
                    break;
                case SyncIntentOutcome.Rejected:
                    _rejected++;
                    break;
                case SyncIntentOutcome.Conflict:
                    _conflicts++;
                    break;
                default:
                    _sent++;
                    break;
            }
        }

        public void Add(SyncApplyReport applied)
        {
            _received += applied.Applied;
            _kept += applied.KeptLocal;
        }

        public SyncReport Report(SyncState state, SyncFailure failure = SyncFailure.None, bool postponed = false) =>
            new(state, failure, Summary, _sent, _conflicts, _rejected, _received, _kept, postponed);
    }

    /// <summary>Le rappel à montrer maintenant, d'après la plus ancienne facture validée sur le poste et pas encore envoyée.</summary>
    public async Task<SyncReminder> ReminderAsync(CancellationToken cancellationToken = default)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
            {
                return SyncReminder.None;
            }

            IReadOnlyList<SyncOutboxItem> pending = await scope.ServiceProvider.GetRequiredService<ISyncOutbox>()
                .ListAsync(SyncOutboxStatus.Pending, cancellationToken).ConfigureAwait(false);
            DateTimeOffset? oldest = pending.Where(i => i.Intent is ValidatedInvoiceFact).Min(i => (DateTimeOffset?)i.Intent.RecordedAt);
            TimeSpan waiting = oldest is { } since ? clock.GetUtcNow() - since : TimeSpan.Zero;
            return waiting >= TimeSpan.FromHours(72) ? SyncReminder.ThreeDays
                : waiting >= TimeSpan.FromHours(24) ? SyncReminder.Day
                : SyncReminder.None;
        }
    }

    private async Task RecordContactAsync(ISyncReplica replica, SyncPolicyResponse? policy, CancellationToken ct)
    {
        await replica.SaveSettingAsync(DeviceSettings.LastContactKey, Millis(clock.GetUtcNow()), ct).ConfigureAwait(false);
        if (policy is not null && OfflinePolicy.IsValid(policy.MaxOfflineHours))
        {
            await replica.SaveSettingAsync(
                DeviceSettings.MaxOfflineHoursKey, policy.MaxOfflineHours.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }
    }

    private static string Millis(DateTimeOffset at) => at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private async Task EnsureDeviceSeriesAsync(Guid dossier, ISyncReplica replica, CancellationToken ct)
    {
        if (await replica.FindDeviceSeriesAsync(NumberSeriesCodes.Invoices, ct).ConfigureAwait(false) is not null)
        {
            return;
        }

        OpenDeviceSeriesResponse opened;
        try
        {
            opened = await transport.OpenDeviceSeriesAsync(dossier, new OpenDeviceSeriesRequest(options.DeviceId), ct).ConfigureAwait(false);
        }
        catch (SyncRefusedException refused) when (refused.Reason == SyncFailure.Forbidden)
        {
            // L'utilisateur ne valide pas de factures : pas de série, la synchronisation reste réussie.
            return;
        }

        foreach (DeviceSeriesResponse series in opened.Series)
        {
            await replica.SaveDeviceSeriesAsync(new DeviceSeries(series.BaseSeries, series.SeriesCode, series.Prefix, options.DeviceId, series.Next), ct).ConfigureAwait(false);
        }
    }

    internal static SyncSummary Summarize(IReadOnlyList<SyncOutboxItem> pending) => new(
        Creations: pending.Count(i => i.Intent is CreateCustomerIntent or CreateSupplierIntent or CreateCatalogItemIntent),
        Changes: pending.Count(i => i.Intent is not (CreateCustomerIntent or CreateSupplierIntent or CreateCatalogItemIntent or ValidatedInvoiceFact)),
        EstimatedBytes: pending.Sum(i => (long)Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(i.Intent, ContractsJsonContext.Default.SyncIntent))),
        Validations: pending.Count(i => i.Intent is ValidatedInvoiceFact));

    public void Dispose() => _gate.Dispose();

    private void Enter(SyncState state)
    {
        State = state;
        _history.Add(state);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
