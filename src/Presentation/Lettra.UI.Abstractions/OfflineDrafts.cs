namespace Lettra.UI.Abstractions;

public enum OfflineDraftKind
{
    Quote = 1,
    Invoice = 2,
}

/// <summary>
/// Un brouillon de devis ou de facture gardé sur l'appareil faute de réseau (étape 4.5, navigateur). Le contenu est la
/// requête d'enregistrement en JSON, avec son empreinte SHA-256 : elle est revérifiée avant l'envoi.
/// </summary>
/// <param name="LocalId">Identifiant sur l'appareil ; une pièce jamais envoyée n'a que lui.</param>
/// <param name="Kind">Devis ou facture.</param>
/// <param name="ServerId">La pièce du serveur, si elle existe déjà (modifiée hors ligne, ou créée par un envoi).</param>
/// <param name="BaseVersion">Version du serveur sur laquelle la saisie s'appuie (concurrence optimiste).</param>
/// <param name="Title">Nom du client, pour la liste des brouillons en attente.</param>
/// <param name="Payload">Requête d'enregistrement (<c>SaveQuoteRequest</c> ou <c>SaveInvoiceRequest</c>) en JSON.</param>
/// <param name="ContentHash">SHA-256 de <paramref name="Payload"/>, en hexadécimal.</param>
/// <param name="SavedAt">Dernier enregistrement sur l'appareil.</param>
/// <param name="LastError">Code du dernier refus du serveur (conflit de version, contenu altéré…), sinon rien.</param>
/// <param name="DossierId">Dossier dans lequel la saisie a été faite : le brouillon ne part que vers lui.</param>
public sealed record OfflineDraft(
    Guid LocalId,
    OfflineDraftKind Kind,
    Guid? ServerId,
    long? BaseVersion,
    string? Title,
    string Payload,
    string ContentHash,
    DateTimeOffset SavedAt,
    string? LastError = null,
    Guid? DossierId = null);

/// <summary>
/// Stockage de l'appareil (navigateur : IndexedDB) : les brouillons en attente, et un cache des données de référence
/// pour rouvrir un brouillon sans réseau. Il est propre à l'utilisateur connecté et au dossier ouvert : sans eux (session
/// inconnue, aucun dossier), il est indisponible, ne liste rien et n'enregistre rien.
/// </summary>
public interface IOfflineDraftStore
{
    /// <summary>L'utilisateur et le dossier sont connus : le stockage qui leur est propre peut servir.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>Efface les brouillons et le cache de l'utilisateur et du dossier (déconnexion).</summary>
    Task ClearAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OfflineDraft>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(OfflineDraft draft, CancellationToken cancellationToken);

    Task RemoveAsync(Guid localId, CancellationToken cancellationToken);

    Task<string?> ReadCacheAsync(string key, CancellationToken cancellationToken);

    Task WriteCacheAsync(string key, string value, CancellationToken cancellationToken);
}
