using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Où l'application installée (Windows, macOS, Android, iOS) range ses données : sur cet appareil seul, ou sur le serveur
/// commun du cabinet, que le navigateur et les autres appareils partagent. Fourni par l'hôte MAUI ; absent dans le
/// navigateur, qui travaille toujours sur le serveur qui l'a servi.
/// </summary>
public interface IServerConnection
{
    /// <summary>Le serveur commun utilisé, ou <c>null</c> quand l'appareil travaille seul.</summary>
    Uri? CurrentServer { get; }

    /// <summary>Le choix enregistré prendra effet au prochain démarrage (enregistré pendant cette session).</summary>
    bool RestartPending { get; }

    /// <summary>Comment l'application redémarre sur cet appareil après un changement.</summary>
    ServerRestartKind RestartKind { get; }

    /// <summary>
    /// Vérifie l'adresse saisie (serveur Lettra joignable, certificat approuvé) puis l'enregistre : l'application
    /// travaillera sur ce serveur après redémarrage. Renvoie l'adresse retenue. Les données de l'appareil restent en place.
    /// </summary>
    Task<OperationResult<Uri>> UseServerAsync(string address, CancellationToken cancellationToken);

    /// <summary>Revient au travail sur cet appareil seul, après redémarrage, avec les données déjà présentes sur l'appareil.</summary>
    Task UseThisDeviceOnlyAsync(CancellationToken cancellationToken);

    /// <summary>Redémarre l'application (<see cref="ServerRestartKind.Automatic"/>) ou la ferme (<see cref="ServerRestartKind.CloseThenReopen"/>).</summary>
    void Restart();
}

public enum ServerRestartKind
{
    /// <summary>L'application se relance d'elle-même (Windows).</summary>
    Automatic,

    /// <summary>L'application se ferme ; l'utilisateur la rouvre (Android, macOS).</summary>
    CloseThenReopen,

    /// <summary>L'application ne peut pas se fermer d'elle-même : l'utilisateur la ferme puis la rouvre (iOS).</summary>
    Manual,
}
