using System.Text.Json;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Sync.Client;

/// <summary>
/// La synchronisation du poste vue par l'UI (étape 4.3) : adaptateur de <see cref="SyncCoordinator"/> vers
/// <see cref="ISyncCenter"/>. L'instantané est relu après chaque changement d'état du coordinateur, chaque cycle et
/// chaque saisie qui laisse une intention ; <see cref="Changed"/> part alors de n'importe quel thread.
/// </summary>
internal sealed partial class SyncCenter : ISyncCenter, IDisposable
{
    private readonly SyncCoordinator _coordinator;
    private readonly ISyncPreferences _preferences;
    private readonly IServiceScopeFactory _scopes;
    private readonly SyncOutboxSignal _signal;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncCenter> _logger;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private SyncReport? _lastReport;
    private SyncCycleReport? _lastCycle;

    public SyncCenter(SyncCoordinator coordinator, ISyncPreferences preferences, IServiceScopeFactory scopes, SyncOutboxSignal signal, TimeProvider clock, ILogger<SyncCenter> logger)
    {
        _coordinator = coordinator;
        _preferences = preferences;
        _scopes = scopes;
        _signal = signal;
        _clock = clock;
        _logger = logger;
        _coordinator.Changed += OnSourceChanged;
        _signal.Recorded += OnSourceChanged;
        OnSourceChanged(this, EventArgs.Empty);
    }

    public SyncSnapshot Current { get; private set; } = SyncSnapshot.Initial;

    public SyncMode Mode { get; private set; } = SyncMode.AlwaysAsk;

    public event EventHandler? Changed;

    public async Task<SyncCycleReport> SyncNowAsync(CancellationToken cancellationToken)
    {
        SyncReport report = await _coordinator.RunAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return _lastReport == report && _lastCycle is not null ? _lastCycle : Map(report, _clock.GetUtcNow());
    }

    public async Task SetModeAsync(SyncMode mode, CancellationToken cancellationToken)
    {
        await _preferences.SetAsync((SyncPreference)(int)mode, cancellationToken).ConfigureAwait(false);
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<SyncPendingItem>> ListPendingAsync(CancellationToken cancellationToken) =>
        InDossierAsync<IReadOnlyList<SyncPendingItem>>(
            async sp => [.. (await sp.GetRequiredService<ISyncOutbox>().ListAsync(SyncOutboxStatus.Pending, cancellationToken).ConfigureAwait(false)).Select(SyncItems.Pending)],
            [],
            cancellationToken);

    public Task<IReadOnlyList<SyncRejectedItem>> ListRejectedAsync(CancellationToken cancellationToken) =>
        InDossierAsync<IReadOnlyList<SyncRejectedItem>>(
            async sp => [.. (await sp.GetRequiredService<ISyncOutbox>().ListAsync(SyncOutboxStatus.Rejected, cancellationToken).ConfigureAwait(false)).Select(SyncItems.Rejected)],
            [],
            cancellationToken);

    public Task<IReadOnlyList<SyncFieldDispute>> ListConflictsAsync(CancellationToken cancellationToken) =>
        InDossierAsync<IReadOnlyList<SyncFieldDispute>>(
            async sp =>
            {
                List<SyncFieldDispute> disputes = [];
                Dictionary<(SyncEntityKind, Guid), string?> labels = [];
                foreach (SyncConflictRecord conflict in await sp.GetRequiredService<ISyncOutbox>().ListConflictsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!labels.TryGetValue((conflict.Entity, conflict.EntityId), out string? label))
                    {
                        label = await LabelAsync(sp, conflict.Entity, conflict.EntityId, cancellationToken).ConfigureAwait(false);
                        labels[(conflict.Entity, conflict.EntityId)] = label;
                    }

                    disputes.Add(new SyncFieldDispute(
                        conflict.Id,
                        conflict.IdempotencyKey,
                        (SyncItemKind)(int)conflict.Entity,
                        conflict.EntityId,
                        label,
                        conflict.Conflict.Field,
                        Display(conflict.Conflict.Base),
                        Display(conflict.Conflict.Mine),
                        Display(conflict.Conflict.Server),
                        conflict.DetectedAt));
                }

                return disputes;
            },
            [],
            cancellationToken);

    public async Task<OperationResult<bool>> ResolveAsync(IReadOnlyList<SyncFieldChoice> choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choices);
        IReadOnlyList<SyncConflictRecord> conflicts = await InDossierAsync<IReadOnlyList<SyncConflictRecord>>(
            sp => sp.GetRequiredService<ISyncOutbox>().ListConflictsAsync(cancellationToken), [], cancellationToken).ConfigureAwait(false);

        // Les choix d'une même saisie arrivent ensemble ; le premier refus est rendu, les choix suivants ne sont pas tentés.
        IReadOnlyList<Error> refused = [];
        foreach (SyncFieldChoice choice in choices)
        {
            string? field = conflicts.FirstOrDefault(c => c.Id == choice.ConflictId)?.Conflict.Field;
            Result resolved = await _coordinator.ResolveConflictAsync(
                choice.ConflictId, choice.KeepMine ? SyncResolution.KeepMine : SyncResolution.TakeServer, cancellationToken).ConfigureAwait(false);
            if (resolved.IsFailure)
            {
                refused = [.. resolved.Errors.Select(e => e with { Target = field })];
                break;
            }
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return refused.Count == 0 ? OperationResult<bool>.Success(true) : OperationResult<bool>.Failure(refused.ToErrorDetails());
    }

    /// <summary>Relit l'instantané (outbox, conflits, refus, rappel, préférence) et prévient l'UI.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Mode = (SyncMode)(int)await _preferences.GetAsync(cancellationToken).ConfigureAwait(false);
            SyncReminder reminder = await _coordinator.ReminderAsync(cancellationToken).ConfigureAwait(false);
            (SyncPendingSummary pending, int conflicts, int rejected) = await InDossierAsync(
                async sp =>
                {
                    ISyncOutbox outbox = sp.GetRequiredService<ISyncOutbox>();
                    IReadOnlyList<SyncOutboxItem> waiting = await outbox.ListAsync(SyncOutboxStatus.Pending, cancellationToken).ConfigureAwait(false);
                    SyncSummary summary = SyncCoordinator.Summarize(waiting);
                    return (
                        new SyncPendingSummary(summary.Creations, summary.Changes, summary.EstimatedBytes, summary.Validations),
                        (await outbox.ListConflictsAsync(cancellationToken).ConfigureAwait(false)).Count,
                        (await outbox.ListAsync(SyncOutboxStatus.Rejected, cancellationToken).ConfigureAwait(false)).Count);
                },
                (SyncPendingSummary.Empty, 0, 0),
                cancellationToken).ConfigureAwait(false);

            if (_coordinator.LastReport is { } last && !ReferenceEquals(last, _lastReport))
            {
                _lastReport = last;
                _lastCycle = Map(last, _clock.GetUtcNow());
            }

            Current = new SyncSnapshot((SyncStatus)(int)_coordinator.State, pending, (SyncReminderLevel)(int)reminder, conflicts, rejected, _lastCycle);
        }
        finally
        {
            _refresh.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _coordinator.Changed -= OnSourceChanged;
        _signal.Recorded -= OnSourceChanged;
        _refresh.Dispose();
    }

    private async void OnSourceChanged(object? sender, EventArgs e)
    {
        try
        {
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Un évènement ne doit jamais faire tomber l'application : l'instantané reste le précédent.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogRefreshFailed(_logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rafraîchissement de l'état de synchronisation impossible ({ExceptionType}) : l'écran garde le précédent.")]
    private static partial void LogRefreshFailed(ILogger logger, string exceptionType);

    private static SyncCycleReport Map(SyncReport r, DateTimeOffset finishedAt) => new(
        (SyncStatus)(int)r.State,
        (SyncFailureKind)(int)r.Failure,
        new SyncPendingSummary(r.Summary.Creations, r.Summary.Changes, r.Summary.EstimatedBytes, r.Summary.Validations),
        r.Sent,
        r.Conflicts,
        r.Rejected,
        r.Received,
        r.KeptLocal,
        r.Postponed,
        finishedAt);

    /// <summary>Valeur prête à afficher : une chaîne JSON donne son texte, <c>null</c> rien, le reste son écriture brute.</summary>
    internal static string? Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };

    private static async Task<string?> LabelAsync(IServiceProvider sp, SyncEntityKind entity, Guid id, CancellationToken ct) => entity switch
    {
        SyncEntityKind.Customer => (await sp.GetRequiredService<ICustomerReadStore>().GetAsync(new CustomerId(id), ct).ConfigureAwait(false))?.Name,
        SyncEntityKind.Supplier => (await sp.GetRequiredService<ISupplierReadStore>().GetAsync(new SupplierId(id), ct).ConfigureAwait(false))?.Name,
        SyncEntityKind.CatalogItem => (await sp.GetRequiredService<ICatalogItemReadStore>().GetAsync(new CatalogItemId(id), ct).ConfigureAwait(false))?.Designation,
        _ => null,
    };

    private async Task<T> InDossierAsync<T>(Func<IServiceProvider, Task<T>> work, T closed, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = _scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
            {
                return closed;
            }

            return await work(scope.ServiceProvider).ConfigureAwait(false);
        }
    }
}

/// <summary>Une saisie vient de laisser une intention dans l'outbox : le résumé en attente a changé.</summary>
internal sealed class SyncOutboxSignal
{
    public event EventHandler? Recorded;

    public void Notify() => Recorded?.Invoke(this, EventArgs.Empty);
}

/// <summary>Ce que l'utilisateur reconnaît d'une intention : nom du tiers, désignation, numéro de facture.</summary>
internal static class SyncItems
{
    public static SyncPendingItem Pending(SyncOutboxItem item) =>
        new(item.Sequence, Kind(item.Intent), Change(item.Intent), item.Intent.EntityId, Label(item.Intent), item.Intent.RecordedAt);

    public static SyncRejectedItem Rejected(SyncOutboxItem item) =>
        new(item.Sequence, Kind(item.Intent), Change(item.Intent), item.Intent.EntityId, Label(item.Intent),
            item.Detail is { Length: > 0 } detail ? detail.Split(' ', StringSplitOptions.RemoveEmptyEntries) : []);

    private static SyncItemKind Kind(SyncIntent intent) => (SyncItemKind)(int)intent.EntityOf();

    private static SyncChangeKind Change(SyncIntent intent) => intent switch
    {
        CreateCustomerIntent or CreateSupplierIntent or CreateCatalogItemIntent => SyncChangeKind.Creation,
        ValidatedInvoiceFact => SyncChangeKind.Validation,
        _ => SyncChangeKind.Change,
    };

    private static string? Label(SyncIntent intent) => intent switch
    {
        CreateCustomerIntent c => c.Data.Name,
        UpdateCustomerIntent u => u.Data.Name,
        CreateSupplierIntent c => c.Data.Name,
        UpdateSupplierIntent u => u.Data.Name,
        DeleteCustomerIntent d => d.Name,
        DeleteSupplierIntent d => d.Name,
        CreateCatalogItemIntent c => c.Data.Designation,
        UpdateCatalogItemIntent u => u.Data.Designation,
        ValidatedInvoiceFact f => f.Document.Number,
        _ => null,
    };
}

/// <summary>La demande du coordinateur, posée par le dialogue de l'UI (<see cref="ISyncConsentDialog"/>).</summary>
internal sealed class DialogConsentPrompt(ISyncConsentDialog dialog) : ISyncConsentPrompt
{
    public async Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        SyncAnswer answer = await dialog.AskAsync(
            new SyncPendingSummary(summary.Creations, summary.Changes, summary.EstimatedBytes, summary.Validations),
            (SyncNetwork)(int)network,
            cancellationToken).ConfigureAwait(false);
        return (SyncConsent)(int)answer;
    }

    public Task ShowDetailAsync(IReadOnlyList<SyncOutboxItem> pending, CancellationToken cancellationToken) =>
        dialog.ShowDetailAsync([.. pending.Select(SyncItems.Pending)], cancellationToken);
}
