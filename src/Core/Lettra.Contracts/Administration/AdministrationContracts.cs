using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;

namespace Lettra.Contracts.Administration;

/// <summary>
/// Les grands espaces de Lettra, tels qu'un administrateur les comprend : chacun regroupe des écrans et les permissions
/// qui les ouvrent. Le serveur traduit un niveau d'accès en permissions ; l'interface ne manipule jamais de permission.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessAreaDto>))]
public enum AccessAreaDto
{
    /// <summary>Écran Devis.</summary>
    Quotes = 1,

    /// <summary>Écran Factures.</summary>
    Invoices = 2,

    /// <summary>Écran Avoirs.</summary>
    CreditNotes = 3,

    /// <summary>Écrans Clients et Fournisseurs.</summary>
    Parties = 4,

    /// <summary>Écran Catalogue.</summary>
    Catalog = 5,

    /// <summary>Export FEC, contrôle de la chaîne, clôtures et archives.</summary>
    Accounting = 6,

    /// <summary>Centre d'alertes.</summary>
    Alerts = 7,

    /// <summary>Assistant IA.</summary>
    Assistant = 8,

    /// <summary>Postes hors ligne du dossier.</summary>
    Devices = 9,

    /// <summary>Espace « Équipe et accès » : personnes, équipes et rôles.</summary>
    Administration = 10,

    /// <summary>Portail du client du cabinet.</summary>
    Portal = 11,
}

/// <summary>Niveau d'accès à un espace, du plus faible au plus fort ; chaque niveau comprend les précédents.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessLevelDto>))]
public enum AccessLevelDto
{
    None = 0,

    /// <summary>Voir.</summary>
    View = 1,

    /// <summary>Créer et modifier.</summary>
    Work = 2,

    /// <summary>Valider, émettre, approuver.</summary>
    Approve = 3,
}

/// <summary>Un espace et le niveau qu'on y a.</summary>
public sealed record AreaAccess(AccessAreaDto Area, AccessLevelDto Level);

/// <summary>
/// Description d'un espace pour l'interface : les niveaux qu'il propose, s'il entre dans un rôle sur mesure, et s'il
/// partage le niveau « Voir » avec les autres pièces de vente (devis, factures et avoirs se lisent ensemble).
/// </summary>
public sealed record AccessAreaInfo(AccessAreaDto Area, IReadOnlyList<AccessLevelDto> Levels, bool Customizable, bool SharesSalesView);

/// <summary>Les espaces, dans l'ordre d'affichage. Données pures, partagées par le serveur et l'interface.</summary>
public static class AccessAreas
{
    private static readonly AccessLevelDto[] ViewWork = [AccessLevelDto.View, AccessLevelDto.Work];
    private static readonly AccessLevelDto[] ViewWorkApprove = [AccessLevelDto.View, AccessLevelDto.Work, AccessLevelDto.Approve];
    private static readonly AccessLevelDto[] WorkOnly = [AccessLevelDto.Work];

    public static IReadOnlyList<AccessAreaInfo> All { get; } =
    [
        new(AccessAreaDto.Quotes, ViewWork, Customizable: true, SharesSalesView: true),
        new(AccessAreaDto.Invoices, ViewWorkApprove, Customizable: true, SharesSalesView: true),
        new(AccessAreaDto.CreditNotes, ViewWorkApprove, Customizable: true, SharesSalesView: true),
        new(AccessAreaDto.Parties, ViewWork, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Catalog, ViewWork, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Accounting, ViewWorkApprove, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Alerts, ViewWorkApprove, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Assistant, WorkOnly, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Devices, WorkOnly, Customizable: true, SharesSalesView: false),
        new(AccessAreaDto.Administration, ViewWorkApprove, Customizable: false, SharesSalesView: false),
        new(AccessAreaDto.Portal, ViewWork, Customizable: false, SharesSalesView: false),
    ];

    public static AccessAreaInfo Of(AccessAreaDto area) => All.First(a => a.Area == area);
}

/// <summary>
/// Un rôle tel que l'administrateur le voit : ses espaces et niveaux, où on peut le donner, s'il exige une échéance.
/// Les rôles fournis sont décrits par l'interface à partir de leur clé ; un rôle sur mesure porte son nom.
/// </summary>
/// <param name="Key">Clé stable du rôle.</param>
/// <param name="IsCustom">Rôle composé par le cabinet.</param>
/// <param name="Name">Nom d'un rôle sur mesure.</param>
/// <param name="Description">Description d'un rôle sur mesure.</param>
/// <param name="WidestScope">Portée la plus large où on peut le donner.</param>
/// <param name="RequiresExpiry">Une échéance est obligatoire (auditeur, délégation).</param>
/// <param name="Areas">Espaces ouverts, avec leur niveau.</param>
/// <param name="AssignedCount">Nombre d'attributions en cours.</param>
public sealed record RoleDescription(
    string Key,
    bool IsCustom,
    string? Name,
    string? Description,
    ScopeKindDto WidestScope,
    bool RequiresExpiry,
    IReadOnlyList<AreaAccess> Areas,
    int AssignedCount);

/// <summary>Une personne du cabinet, ses équipes et ses rôles directs.</summary>
public sealed record PersonSummary(
    Guid Id,
    string? DisplayName,
    string? Email,
    AccountStateDto State,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> TeamIds,
    IReadOnlyList<RoleAssignmentSummary> Assignments,
    bool IsMe);

/// <summary>Une équipe (groupe du cabinet) : ses membres, ses dossiers, ses rôles, et si l'utilisateur peut la gérer.</summary>
public sealed record TeamSummary(
    Guid Id,
    string Name,
    IReadOnlyList<Guid> MemberIds,
    IReadOnlyList<Guid> DossierIds,
    IReadOnlyList<RoleAssignmentSummary> Assignments,
    bool CanManage);

/// <summary>Ce que l'utilisateur courant peut faire dans l'espace d'administration.</summary>
/// <param name="ManagePeople">Inviter, modifier, suspendre, donner et retirer des rôles.</param>
/// <param name="ManageTeams">Créer, renommer et supprimer des équipes.</param>
/// <param name="ManageRoles">Composer des rôles sur mesure.</param>
/// <param name="ReadOnly">Le registre ne s'administre pas ici (poste isolé) : tout est en lecture.</param>
public sealed record AdministrationCapabilities(bool ManagePeople, bool ManageTeams, bool ManageRoles, bool ReadOnly);

/// <summary>Tout l'espace d'administration du cabinet en une lecture.</summary>
public sealed record AdministrationOverview(
    Guid CabinetId,
    string? CabinetName,
    AdministrationCapabilities Can,
    IReadOnlyList<PersonSummary> People,
    IReadOnlyList<TeamSummary> Teams,
    IReadOnlyList<Dossiers.DossierSummary> Dossiers,
    IReadOnlyList<RoleDescription> Roles);

/// <summary>
/// Ce que l'utilisateur courant peut voir : son cabinet, s'il peut ouvrir l'espace d'administration, et ses niveaux par
/// espace sur le dossier ouvert (et le cabinet, pour les alertes et l'administration). Sert à n'afficher que les écrans permis.
/// </summary>
public sealed record MyAccessResponse(Guid? CabinetId, bool CanAdminister, IReadOnlyList<AreaAccess> Areas)
{
    public AccessLevelDto LevelOf(AccessAreaDto area) => Areas.FirstOrDefault(a => a.Area == area)?.Level ?? AccessLevelDto.None;
}

/// <summary>Ajoute une personne au cabinet. Mutable pour <c>EditForm</c>.</summary>
public sealed record InvitePersonRequest
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 254;

    [Required(ErrorMessage = ErrorCodes.Administration.NameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Administration.NameTooLong)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    [StringLength(MaxEmailLength, ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Corrige le nom ou l'adresse d'une personne.</summary>
public sealed record UpdatePersonRequest
{
    [Required(ErrorMessage = ErrorCodes.Administration.NameRequired)]
    [StringLength(InvitePersonRequest.MaxNameLength, ErrorMessage = ErrorCodes.Administration.NameTooLong)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    [StringLength(InvitePersonRequest.MaxEmailLength, ErrorMessage = ErrorCodes.Administration.EmailInvalid)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Renomme une équipe.</summary>
public sealed record RenameTeamRequest
{
    [Required(ErrorMessage = ErrorCodes.Rights.GroupNameRequired)]
    [StringLength(CreateGroupRequest.MaxNameLength, ErrorMessage = ErrorCodes.Rights.GroupNameTooLong)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Compose ou modifie un rôle sur mesure : un nom, une phrase, un niveau par espace.</summary>
public sealed record SaveCustomRoleRequest
{
    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 300;

    [Required(ErrorMessage = ErrorCodes.Administration.RoleNameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Administration.RoleNameTooLong)]
    public string Name { get; set; } = string.Empty;

    [StringLength(MaxDescriptionLength, ErrorMessage = ErrorCodes.Administration.RoleDescriptionTooLong)]
    public string? Description { get; set; }

    public List<AreaAccess> Areas { get; set; } = [];
}
