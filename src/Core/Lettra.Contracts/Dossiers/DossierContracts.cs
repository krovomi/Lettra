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
