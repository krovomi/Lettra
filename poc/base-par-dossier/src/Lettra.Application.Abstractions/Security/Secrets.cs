namespace Lettra.Application.Abstractions.Security;

/// Résout une référence de secret en valeur : Key Vault / KMS (SaaS), coffre de l'OS (poste), variables d'environnement (sur site).
public interface ISecretStore
{
    ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken ct = default);
}

/// Hiérarchie de clés de l'ADR-0011 : une clé maîtresse (KEK) par cabinet chiffre une clé (DEK) par dossier.
public interface IDossierKeyring
{
    /// Crée la DEK d'un nouveau dossier et la renvoie chiffrée par la KEK du cabinet (à stocker).
    ValueTask<byte[]> CreateWrappedDekAsync(Guid organisationId, Guid dossierId, CancellationToken ct = default);

    /// Déchiffre la DEK d'un dossier. Lève une exception si la KEK du cabinet est absente ou si l'enveloppe a été altérée.
    ValueTask<byte[]> UnwrapDekAsync(Guid organisationId, Guid dossierId, byte[] wrappedDek, CancellationToken ct = default);
}
