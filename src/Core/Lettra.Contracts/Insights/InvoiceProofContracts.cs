namespace Lettra.Contracts.Insights;

/// <summary>
/// La preuve d'authenticité d'une pièce validée : le texte du QR code imprimé sur le PDF. Il dit qui a émis quoi
/// (SIREN, numéro, date, total TTC) et porte la signature ECDSA P-256 du dossier, vérifiable hors ligne avec la clé
/// publique du registre.
/// </summary>
/// <param name="InvoiceId">La pièce.</param>
/// <param name="Number">Son numéro.</param>
/// <param name="Code">Le texte du QR code (<c>LETTRA-PREUVE-1;…</c>).</param>
/// <param name="KeyId">La version de clé qui a signé.</param>
public sealed record InvoiceProofResponse(Guid InvoiceId, string Number, string Code, string KeyId);

/// <summary>Le texte lu dans le QR code d'une facture (par un lecteur de QR code, ou recopié).</summary>
public sealed record VerifyInvoiceProofRequest
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>Le verdict sur une preuve d'authenticité (valeurs stables, ajout seulement).</summary>
public enum InvoiceProofVerdictDto
{
    /// <summary>Signée par ce dossier, et la pièce du dossier dit la même chose.</summary>
    Authentic = 0,

    /// <summary>Signée par ce dossier, mais la pièce du dossier ne dit plus la même chose (ou n'existe pas).</summary>
    Mismatch = 1,

    /// <summary>La clé est celle de ce dossier, mais la signature ne correspond pas au texte : il a été modifié.</summary>
    Forged = 2,

    /// <summary>La clé n'est pas au registre de ce dossier : la facture n'a pas été émise ici.</summary>
    Foreign = 3,

    /// <summary>Ce n'est pas une preuve Lettra.</summary>
    Unreadable = 4,
}

/// <summary>Ce que la vérification a lu dans la preuve et constaté dans le dossier.</summary>
/// <param name="Verdict">Le verdict.</param>
/// <param name="Siren">SIREN de l'émetteur lu dans la preuve.</param>
/// <param name="Number">Numéro lu dans la preuve.</param>
/// <param name="IssueDate">Date d'émission lue dans la preuve.</param>
/// <param name="TotalInclTax">Total TTC lu dans la preuve.</param>
/// <param name="KeyId">Version de clé lue dans la preuve.</param>
/// <param name="InvoiceId">La pièce du dossier de même numéro, si elle existe.</param>
/// <param name="Differences">Ce qui diffère entre la preuve et la pièce du dossier : <c>Missing</c>, <c>IssueDate</c>, <c>TotalInclTax</c>, <c>Siren</c>.</param>
/// <param name="FullyCredited">La pièce du dossier est entièrement annulée par des avoirs.</param>
public sealed record InvoiceProofCheckResponse(
    InvoiceProofVerdictDto Verdict,
    string? Siren,
    string? Number,
    DateOnly? IssueDate,
    decimal? TotalInclTax,
    string? KeyId,
    Guid? InvoiceId,
    IReadOnlyList<string> Differences,
    bool FullyCredited = false);

/// <summary>Une version du registre des clés publiques du dossier, au format PEM, pour vérifier hors ligne.</summary>
/// <param name="Version">Numéro de version.</param>
/// <param name="KeyId">Identifiant (<c>p256-</c> et 16 caractères hexadécimaux).</param>
/// <param name="PublicKeyPem">Clé publique (SubjectPublicKeyInfo) au format PEM.</param>
/// <param name="CreatedAt">Entrée en vigueur.</param>
/// <param name="RetiredAt">Fin de vigueur (vérification seule ensuite), sinon rien.</param>
public sealed record InvoiceProofKeyResponse(int Version, string KeyId, string PublicKeyPem, DateTimeOffset CreatedAt, DateTimeOffset? RetiredAt);
