namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Enregistrement différé d'un brouillon : chaque saisie repousse l'enregistrement ; il part quand l'utilisateur
/// marque une pause (ou tout de suite par Ctrl+S). Un seul enregistrement à la fois, le dernier état gagne.
/// </summary>
public sealed class DraftAutosave(Func<Task> save, TimeSpan delay) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;

    public bool IsDirty { get; private set; }

    /// <summary>Marque le brouillon modifié et planifie l'enregistrement.</summary>
    public async Task ScheduleAsync()
    {
        IsDirty = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        CancellationToken token = _pending.Token;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token).ConfigureAwait(true);
            }

            await FlushAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Une saisie plus récente a repoussé l'enregistrement.
        }
    }

    /// <summary>Enregistre tout de suite ce qui est en attente (Ctrl+S, validation, PDF).</summary>
    public Task FlushAsync() => FlushAsync(CancellationToken.None);

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!IsDirty)
            {
                return;
            }

            IsDirty = false;
            await save().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Le brouillon a été rechargé ou abandonné : plus rien à enregistrer.</summary>
    public void Discard()
    {
        _pending?.Cancel();
        IsDirty = false;
    }

    public void Dispose()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _gate.Dispose();
    }
}
