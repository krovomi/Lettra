using Lettra.UI.Abstractions;

namespace Lettra.UI.Shared.Sync;

/// <summary>Une demande affichée : ce qui partirait et sur quel réseau.</summary>
public sealed record SyncConsentRequest(SyncPendingSummary Summary, SyncNetwork Network);

/// <summary>
/// Demande « Maintenant / Plus tard / Détail » au retour du réseau (ADR-0008). Non bloquante : la demande attend la
/// réponse pendant que l'utilisateur continue de travailler ; <see cref="Components.Sync.SyncConsentPanel"/> l'affiche.
/// Singleton : le cycle de synchronisation du poste l'appelle hors de tout composant.
/// </summary>
public sealed class SyncConsentDialog : ISyncConsentDialog
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<SyncAnswer>? _pending;

    /// <summary>Demande en cours, <c>null</c> quand rien n'est demandé.</summary>
    public SyncConsentRequest? Request { get; private set; }

    /// <summary>Saisies en attente, après « Détail » ; <c>null</c> tant que l'utilisateur ne les a pas demandées.</summary>
    public IReadOnlyList<SyncPendingItem>? Detail { get; private set; }

    public event EventHandler? Changed;

    public async Task<SyncAnswer> AskAsync(SyncPendingSummary summary, SyncNetwork network, CancellationToken cancellationToken)
    {
        TaskCompletionSource<SyncAnswer> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<SyncAnswer>? previous;
        lock (_gate)
        {
            previous = _pending;
            _pending = answer;
            Request = new SyncConsentRequest(summary, network);
        }

        // Une demande plus récente remplace l'ancienne, qui vaut « plus tard ».
        previous?.TrySetResult(SyncAnswer.Later);
        OnChanged();
        try
        {
            return await answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Close(answer);
            throw;
        }
    }

    public Task ShowDetailAsync(IReadOnlyList<SyncPendingItem> pending, CancellationToken cancellationToken)
    {
        Detail = pending;
        OnChanged();
        return Task.CompletedTask;
    }

    /// <summary>Réponse de l'utilisateur. « Détail » garde la demande ouverte : elle est reposée avec la liste.</summary>
    public void Answer(SyncAnswer answer)
    {
        TaskCompletionSource<SyncAnswer>? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
            if (answer != SyncAnswer.ShowDetail)
            {
                Request = null;
                Detail = null;
            }
        }

        pending?.TrySetResult(answer);
        OnChanged();
    }

    private void Close(TaskCompletionSource<SyncAnswer> answer)
    {
        lock (_gate)
        {
            if (_pending != answer)
            {
                return;
            }

            _pending = null;
            Request = null;
            Detail = null;
        }

        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
