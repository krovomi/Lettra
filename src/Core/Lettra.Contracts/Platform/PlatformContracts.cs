using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Platform;

/// <summary>
/// Arrivée d'un cabinet sur la plateforme : son nom, son premier administrateur, et la cellule qui l'héberge (la seule
/// cellule du serveur si elle est omise). Mutable pour <c>EditForm</c>.
/// </summary>
public sealed record OnboardCabinetRequest
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 254;

    [Required(ErrorMessage = ErrorCodes.CabinetLifecycle.NameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.CabinetLifecycle.NameTooLong)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.Administration.NameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Administration.NameTooLong)]
    public string AdministratorName { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    [EmailAddress(ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    [StringLength(MaxEmailLength, ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    public string AdministratorEmail { get; set; } = string.Empty;

    public string? CellId { get; set; }
}

/// <summary>Où en est un cabinet sur la plateforme.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformCabinetStateDto>))]
public enum PlatformCabinetStateDto
{
    /// <summary>Le cabinet travaille.</summary>
    Active = 1,

    /// <summary>Départ demandé : le cabinet travaille et exporte ses données jusqu'à la fin du délai de rétractation.</summary>
    Leaving = 2,

    /// <summary>Parti : dossiers effacés, clé détruite, comptes supprimés. Seule la trace du départ reste.</summary>
    Departed = 3,
}

/// <summary>Un dossier dont la création a échoué, avec la cause technique (nom du type d'erreur, jamais de donnée).</summary>
public sealed record PlatformDossierIssue(Guid Id, string Name, string? LastError);

/// <summary>Un cabinet vu par l'administrateur de la plateforme : ni ses données, ni ses personnes, seulement des compteurs.</summary>
public sealed record PlatformCabinetResponse(
    Guid Id,
    string Name,
    string CellId,
    PlatformCabinetStateDto State,
    bool Declared,
    int ActiveDossiers,
    int ErasureScheduled,
    IReadOnlyList<PlatformDossierIssue> FailedDossiers,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DepartureDueAt,
    DateTimeOffset? DepartedAt);

/// <summary>La plateforme : ses cellules (pour choisir où héberger un nouveau cabinet) et ses cabinets.</summary>
public sealed record PlatformOverviewResponse(bool CanOnboard, IReadOnlyList<string> Cells, IReadOnlyList<PlatformCabinetResponse> Cabinets);

/// <summary>Cabinet inscrit : son administrateur a un compte, et l'e-mail pour choisir son mot de passe est parti ou non.</summary>
public sealed record OnboardCabinetResponse(PlatformCabinetResponse Cabinet, Guid AdministratorId, InvitationEmailStatusDto Invitation);

/// <summary>Créations de dossiers en échec relancées.</summary>
public sealed record DossierRetryResponse(int Resumed);
