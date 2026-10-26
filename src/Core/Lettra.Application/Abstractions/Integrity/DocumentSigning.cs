using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Integrity;

/// <summary>Une pièce signée : la version de clé du registre, le texte signé et la signature (ECDSA P-256, DER).</summary>
public sealed record DocumentSignature(string KeyId, string Statement, byte[] Signature);

/// <summary>Ce que dit la vérification d'une signature de pièce.</summary>
public enum DocumentSignatureCheck
{
    /// <summary>Signée par une version du registre de ce dossier.</summary>
    Valid = 0,

    /// <summary>La version de clé est celle du dossier, mais la signature ne correspond pas au texte : pièce modifiée.</summary>
    Invalid = 1,

    /// <summary>Aucune version du registre de ce dossier ne porte cet identifiant : la pièce vient d'ailleurs.</summary>
    UnknownKey = 2,
}

/// <summary>
/// Signature des pièces émises par la clé de signature en vigueur du dossier (ADR-0011), celle qui signe déjà les
/// clôtures. Le texte signé commence par son propre préfixe : il ne peut pas passer pour un hachage de clôture.
/// La clé privée ne sort jamais de son enveloppe ; la clé publique est au registre, pour une vérification hors ligne.
/// </summary>
public interface IDocumentSigner
{
    /// <summary>
    /// Signe le texte que <paramref name="statementFor"/> écrit pour la version de clé en vigueur (le texte peut la
    /// nommer) ; échoue si le dossier n'a encore aucune version de clé.
    /// </summary>
    Task<Result<DocumentSignature>> SignAsync(Func<string, string> statementFor, CancellationToken cancellationToken);

    /// <summary>Vérifie une signature avec la version <paramref name="keyId"/> du registre, retirée ou non.</summary>
    Task<Result<DocumentSignatureCheck>> VerifyAsync(string keyId, string statement, byte[] signature, CancellationToken cancellationToken);
}
