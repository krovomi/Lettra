namespace Lettra.UI.Abstractions;

/// <summary>
/// État du réseau de l'appareil, quand l'hôte le connaît (navigateur : <c>navigator.onLine</c>). Hors ligne, les
/// gateways HTTP répondent « serveur injoignable » sans envoyer de requête vouée à l'échec.
/// </summary>
public interface INetworkStatus
{
    ValueTask<bool> IsOfflineAsync(CancellationToken cancellationToken);
}
