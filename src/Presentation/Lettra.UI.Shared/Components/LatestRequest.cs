namespace Lettra.UI.Shared.Components;

/// <summary>
/// La dernière d'une suite de demandes (recherche au fil de la frappe, fiche sélectionnée) : en lancer une annule la
/// précédente, si bien qu'une réponse lente arrivée après une demande plus récente n'écrase jamais l'écran. Fermer
/// l'écran annule la demande en cours.
/// </summary>
public sealed class LatestRequest : IDisposable
{
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <summary>Annule la demande précédente et renvoie le jeton de la nouvelle (déjà annulé une fois l'écran fermé).</summary>
    public CancellationToken Start()
    {
        Cancel();
        if (_disposed)
        {
            return new CancellationToken(canceled: true);
        }

        _current = new CancellationTokenSource();
        return _current.Token;
    }

    /// <summary>Le jeton de la demande en cours, pour une suite qui la prolonge (page suivante de la même recherche).</summary>
    public CancellationToken Current => _disposed ? new CancellationToken(canceled: true) : _current?.Token ?? CancellationToken.None;

    /// <summary>Lance <paramref name="call"/> comme nouvelle demande : <c>null</c> si une plus récente l'a remplacée entre-temps.</summary>
    public Task<T?> RunAsync<T>(Func<CancellationToken, Task<T>> call)
        where T : class => RunAsync(call, Start());

    /// <summary>Prolonge la demande en cours (page suivante) : <c>null</c> si une nouvelle demande l'a remplacée entre-temps.</summary>
    public Task<T?> ContinueAsync<T>(Func<CancellationToken, Task<T>> call)
        where T : class => RunAsync(call, Current);

    public void Dispose()
    {
        _disposed = true;
        Cancel();
    }

    private static async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken token)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            T result = await call(token);
            return token.IsCancellationRequested ? null : result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
    }

    private void Cancel()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }
}
