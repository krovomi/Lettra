using System.Text;
using Lettra.Poc.Sync.Contracts;

namespace Lettra.Poc.Sync.Client;

/// <summary>
/// Cycle de synchronisation (annexe A §3.7) : sonde, résumé, demande à l'utilisateur,
/// authentification, faits, intentions, réception, conflits, compte rendu.
/// Une coupure à n'importe quelle étape laisse l'outbox intacte pour ce qui n'a pas été acquitté.
/// </summary>
public sealed class SyncCoordinator(
    LocalStore store,
    INetworkProbe probe,
    ISyncConsentPrompt consent,
    ISyncClient client,
    Func<SyncPreference> preference,
    TimeProvider clock,
    int batchSize = 50)
{
    public const string AuditOrigin = "Synchronisation";
    public const string SyncTokenKey = "sync.token";

    private readonly List<SyncState> _history = [];

    public SyncState State { get; private set; } = SyncState.Offline;

    /// <summary>États traversés au dernier cycle (pour l'UI et les tests).</summary>
    public IReadOnlyList<SyncState> History => _history;

    public async Task<SyncReport> OnNetworkChangedAsync(CancellationToken ct = default)
    {
        _history.Clear();
        var messages = new List<string>();
        var summary = Summarize();
        int accepted = 0, rejected = 0, applied = 0, conflicts = 0, pulled = 0;
        SyncReport Report() => new(State, summary, accepted, rejected, applied, conflicts, pulled, messages);

        // 1. Détection confirmée par une sonde réelle.
        var network = await probe.ProbeAsync(ct);
        if (!network.ServerReachable) { Enter(SyncState.Offline); return Report(); }

        // 2-3. Résumé et demande à l'utilisateur (sauf préférence contraire, ou rien à envoyer).
        if (!summary.IsEmpty)
        {
            var pref = preference();
            if (pref == SyncPreference.NeverOnCellular && network.Kind == NetworkKind.Cellular)
            {
                Enter(SyncState.PendingConsent);
                messages.Add("Réseau mobile : synchronisation reportée (préférence « jamais en réseau mobile »).");
                return Report();
            }

            var auto = pref == SyncPreference.AutoOnWifi && network.Kind is NetworkKind.Wifi or NetworkKind.Ethernet;
            if (!auto)
            {
                Enter(SyncState.PendingConsent);
                SyncConsent answer;
                while ((answer = await consent.AskAsync(summary, network.Kind, ct)) == SyncConsent.ShowDetail)
                    await consent.ShowDetailAsync(Detail(), ct);
                if (answer == SyncConsent.Later) { messages.Add("Synchronisation reportée par l'utilisateur."); return Report(); }
            }
        }

        try
        {
            // 4. Authentification avant toute écriture.
            Enter(SyncState.Authenticating);
            await client.EnsureAuthenticatedAsync(ct);

            // 5. Envoi : les faits d'abord, puis les intentions, par lots.
            Enter(SyncState.Pushing);
            (accepted, rejected) = await PushFactsAsync(messages, ct);
            (applied, conflicts) = await PushIntentsAsync(messages, ct);

            // 6. Réception depuis le jeton de synchro.
            Enter(SyncState.Pulling);
            pulled = await PullAsync(ct);

            // 7. Conflits : les éléments sans conflit sont déjà appliqués, le reste attend l'écran de résolution.
            var open = store.Conflicts().Count;
            Enter(open > 0 ? SyncState.ResolvingConflicts : SyncState.UpToDate);

            // 8. Compte rendu et audit.
            store.Audit(null, clock.GetUtcNow(), AuditOrigin,
                $"{summary.Describe()} | faits acceptés {accepted}, rejetés {rejected} | intentions appliquées {applied}, conflits {conflicts} | reçus {pulled}");
            return Report();
        }
        catch (DeviceRevokedException e)
        {
            store.Wipe();
            Enter(SyncState.Revoked);
            messages.Add(e.Message);
            return Report();
        }
        catch (ReauthenticationRequiredException e)
        {
            Enter(SyncState.Failed);
            messages.Add(e.Message);
            return Report();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            // Coupure : rien n'est perdu, rien n'est doublé. On reprendra au prochain retour du réseau.
            Enter(SyncState.Failed);
            messages.Add("Connexion perdue pendant la synchronisation : reprise au prochain retour du réseau.");
            return Report();
        }
    }

    /// <summary>Rappel croissant tant que des factures validées hors ligne ne sont pas parties (24 h, 72 h).</summary>
    public ReminderLevel Reminder()
    {
        var oldest = store.Outbox(OutboxStatus.Pending, OutboxKind.Fact).Select(e => (DateTimeOffset?)e.CreatedAt).FirstOrDefault();
        if (oldest is null) return ReminderLevel.None;
        var age = clock.GetUtcNow() - oldest.Value;
        return age >= TimeSpan.FromHours(72) ? ReminderLevel.Alert
             : age >= TimeSpan.FromHours(24) ? ReminderLevel.Banner
             : ReminderLevel.None;
    }

    public SyncSummary Summarize()
    {
        var pending = PendingToSend();
        var intents = pending.Where(e => e.Kind == OutboxKind.Intent)
            .Select(e => LocalStore.Deserialize<DraftCommand>(e.Payload)).ToList();
        return new SyncSummary(
            ValidatedInvoices: pending.Count(e => e.Kind == OutboxKind.Fact),
            DraftChanges: intents.Count(c => c is not ValidateDraftCommand),
            PendingValidations: intents.Count(c => c is ValidateDraftCommand),
            EstimatedBytes: pending.Sum(e => (long)Encoding.UTF8.GetByteCount(e.Payload)));
    }

    /// <summary>
    /// Ce qui part réellement. Les intentions d'une facture validée sur le poste (création, retouches
    /// avant validation) sont couvertes par le fait, qui porte le contenu final : on ne les envoie pas.
    /// </summary>
    private List<OutboxEntry> PendingToSend()
    {
        var all = store.Outbox();
        var validatedHere = all.Where(e => e.Kind == OutboxKind.Fact).Select(e => e.InvoiceId).ToHashSet();
        return [.. all.Where(e => e.Status == OutboxStatus.Pending && (e.Kind == OutboxKind.Fact || !validatedHere.Contains(e.InvoiceId)))];
    }

    private IReadOnlyList<OutboxItemView> Detail() => [.. PendingToSend().Select(e => new OutboxItemView(
        e.Sequence, e.Kind.ToString(), e.InvoiceId,
        e.Kind == OutboxKind.Fact
            ? $"Facture validée {LocalStore.Deserialize<ValidatedInvoiceFact>(e.Payload).Invoice.Number}"
            : LocalStore.Deserialize<DraftCommand>(e.Payload) switch
            {
                CreateDraftCommand c => $"Nouveau brouillon pour {c.Draft.CustomerName}",
                UpdateDraftCommand u => $"Brouillon modifié : {string.Join(", ", u.Changes.Keys)}",
                ValidateDraftCommand => "Validation demandée (numéro attribué par le serveur)",
                _ => "Commande",
            },
        e.CreatedAt))];

    private async Task<(int Accepted, int Rejected)> PushFactsAsync(List<string> messages, CancellationToken ct)
    {
        int accepted = 0, rejected = 0;
        foreach (var batch in store.Outbox(OutboxStatus.Pending, OutboxKind.Fact).Chunk(batchSize))
        {
            var facts = batch.Select(e => LocalStore.Deserialize<ValidatedInvoiceFact>(e.Payload)).ToList();
            var response = await client.PushFactsAsync(facts, ct);

            // Acquittement local seulement après réponse : si elle se perd, le renvoi est idempotent.
            store.InTransaction(tx =>
            {
                foreach (var r in response.Results)
                {
                    var ok = r.Outcome is FactOutcome.Accepted or FactOutcome.AlreadyKnown;
                    store.MarkOutbox(tx, r.IdempotencyKey, ok ? OutboxStatus.Sent : OutboxStatus.Rejected, r.Detail);
                    if (ok) accepted++; else rejected++;
                    if (r.Detail is not null) messages.Add(r.Detail);
                }
            });
        }
        return (accepted, rejected);
    }

    private async Task<(int Applied, int Conflicts)> PushIntentsAsync(List<string> messages, CancellationToken ct)
    {
        int applied = 0, conflicts = 0;
        var toSend = PendingToSend().Where(e => e.Kind == OutboxKind.Intent).ToList();
        var superseded = store.Outbox(OutboxStatus.Pending, OutboxKind.Intent).ExceptBy(toSend.Select(e => e.Sequence), e => e.Sequence).ToList();
        store.InTransaction(tx =>
        {
            foreach (var e in superseded)
                store.MarkOutbox(tx, e.IdempotencyKey, OutboxStatus.Sent, "Couverte par la facture validée sur le poste.");
        });

        foreach (var batch in toSend.Chunk(batchSize))
        {
            var commands = batch.Select(e => LocalStore.Deserialize<DraftCommand>(e.Payload)).ToList();
            var response = await client.PushIntentsAsync(commands, ct);

            store.InTransaction(tx =>
            {
                foreach (var r in response.Results)
                {
                    var status = r.Outcome switch
                    {
                        IntentOutcome.Rejected => OutboxStatus.Rejected,
                        IntentOutcome.Conflict => OutboxStatus.Conflict,
                        _ => OutboxStatus.Sent,
                    };
                    store.MarkOutbox(tx, r.IdempotencyKey, status, r.Detail);
                    foreach (var c in r.Conflicts) store.SaveConflict(tx, r.InvoiceId, c);

                    var local = store.FindInvoice(r.InvoiceId, tx);
                    if (local is not null && r.NewVersion is { } v)
                        store.SaveInvoice(tx, local.Invoice, v, local.PendingValidation && r.Outcome != IntentOutcome.Conflict);

                    if (r.Outcome is IntentOutcome.Applied or IntentOutcome.Merged) applied++;
                    if (r.Outcome == IntentOutcome.Conflict) conflicts++;
                    if (r.Outcome is IntentOutcome.Rejected or IntentOutcome.Conflict && r.Detail is not null) messages.Add(r.Detail);
                }
            });
        }
        return (applied, conflicts);
    }

    private async Task<int> PullAsync(CancellationToken ct)
    {
        var since = long.Parse(store.GetMeta(SyncTokenKey) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var changes = await client.PullSinceAsync(since, ct);

        // Application et avancée du jeton dans la même transaction : une coupure ici fait simplement rejouer la réception.
        return store.InTransaction(tx =>
        {
            var count = 0;
            foreach (var server in changes.Invoices)
            {
                var local = store.FindInvoice(server.Id, tx);
                if (local is not null && local.Invoice.Status == InvoiceStatus.Validated && server.Status == InvoiceStatus.Draft) continue;
                if (store.HasPendingIntents(server.Id, tx)) continue;   // saisie locale plus récente : on la garde
                store.SaveInvoice(tx, server, server.Version, pendingValidation: false);
                count++;
            }
            store.SetMeta(tx, SyncTokenKey, changes.SyncToken.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return count;
        });
    }

    private void Enter(SyncState state) { State = state; _history.Add(state); }
}
