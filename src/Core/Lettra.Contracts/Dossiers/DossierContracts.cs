using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Dossiers;

/// <summary>Création d'un dossier client du cabinet. Mutable pour <c>EditForm</c>.</summary>
public sealed record CreateDossierRequest
{
    public const int MaxNameLength = 200;

    [Required(ErrorMessage = ErrorCodes.Tenancy.DossierNameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Tenancy.DossierNameTooLong)]
    public string Name { get; set; } = string.Empty;
}

public sealed record DossierSummary(Guid Id, string Name);

/// <summary>
/// Un dossier dont le cabinet a demandé l'effacement : fermé à tous, il reste annulable jusqu'à
/// <paramref name="ErasureDueAt"/>. Ensuite, sa clé est détruite et sa base supprimée, sans retour possible.
/// </summary>
public sealed record DossierErasureResponse(Guid Id, string Name, DateTimeOffset ErasureDueAt);

/// <summary>Où en est un dossier du cabinet, vu par son administrateur.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CabinetDossierStateDto>))]
public enum CabinetDossierStateDto
{
    /// <summary>Le dossier est ouvert à ceux qui y ont accès.</summary>
    Active = 1,

    /// <summary>Effacement demandé : fermé à tous, annulable jusqu'à la fin du délai de rétractation.</summary>
    ErasureScheduled = 2,

    /// <summary>Base en cours de création (ou création en échec, reprise automatiquement).</summary>
    Creating = 3,
}

/// <summary>
/// Un dossier du cabinet sur l'écran « Dossiers du cabinet ». <paramref name="Erasable"/> : le serveur sait effacer ce
/// dossier (base créée par Lettra en ligne) ; une base déclarée dans la configuration, ou un dossier du poste, ne
/// s'efface pas depuis Lettra.
/// </summary>
public sealed record CabinetDossierResponse(
    Guid Id,
    string Name,
    CabinetDossierStateDto State,
    bool Erasable,
    DateTimeOffset? ErasureDueAt);

/// <summary>
/// Paquet de réversibilité d'un dossier : tout le dossier (base, pièces, chaîne anti-fraude et ses clés) dans un seul
/// fichier chiffré par une phrase secrète choisie par l'administrateur du cabinet, pour le rapatrier sur un serveur du
/// cabinet (modes 2 et 3) ou sur un poste (mode 1), ou le reprendre plus tard en ligne.
/// </summary>
public static class DossierPackageRules
{
    /// <summary>Extension du fichier ; elle se distingue des manifestes <c>.lettra</c> des dossiers d'un poste.</summary>
    public const string FileExtension = ".lettrapaquet";

    /// <summary>Type MIME du fichier : un contenu chiffré opaque.</summary>
    public const string ContentType = "application/octet-stream";

    /// <summary>Longueur minimale de la phrase secrète : le paquet sort du serveur, la phrase est sa seule protection.</summary>
    public const int MinPassphraseLength = 12;

    public const int MaxPassphraseLength = 1024;

    /// <summary>Taille maximale d'un paquet importé (pièces justificatives comprises).</summary>
    public const long MaxPackageLength = 512L * 1024 * 1024;

    /// <summary>
    /// Taille maximale de la requête d'import : le paquet le plus gros, plus l'enveloppe du formulaire (délimiteurs,
    /// en-têtes des champs, phrase secrète). Toute passerelle placée devant l'API doit l'accepter sur cette route.
    /// </summary>
    public const long MaxImportRequestLength = MaxPackageLength + (1L * 1024 * 1024);

    /// <summary>Noms des champs du formulaire d'import (multipart).</summary>
    public const string PackageField = "paquet";

    public const string PassphraseField = "phrase";
}

/// <summary>Export d'un dossier : la phrase secrète qui chiffre le paquet. Mutable pour <c>EditForm</c>.</summary>
public sealed record ExportDossierRequest
{
    [Required(ErrorMessage = ErrorCodes.Tenancy.PassphraseTooShort)]
    [StringLength(DossierPackageRules.MaxPassphraseLength, MinimumLength = DossierPackageRules.MinPassphraseLength, ErrorMessage = ErrorCodes.Tenancy.PassphraseTooShort)]
    public string Passphrase { get; set; } = string.Empty;
}
