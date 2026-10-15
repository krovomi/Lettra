using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Rights;

/// <summary>État d'un compte du cabinet (annexe F §3.3). Seul <see cref="Active"/> donne des droits.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountStateDto>))]
public enum AccountStateDto
{
    Invited = 1,
    Active = 2,
    Suspended = 3,
    Locked = 4,
    Disabled = 5,
}

/// <summary>Niveau d'une portée, du plus large au plus étroit (la plateforme ne s'attribue pas depuis un cabinet).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScopeKindDto>))]
public enum ScopeKindDto
{
    Cabinet = 2,
    Groupe = 3,
    Dossier = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter<SubjectKindDto>))]
public enum SubjectKindDto
{
    User = 1,
    Group = 2,
}

/// <summary>Déclare un compte dans le cabinet ou change son état (suspendre, réactiver).</summary>
public sealed record SetUserAccountRequest
{
    public AccountStateDto State { get; set; } = AccountStateDto.Active;
}

/// <summary>Création d'un groupe du cabinet (pôle, équipe, bureau). Mutable pour <c>EditForm</c>.</summary>
public sealed record CreateGroupRequest
{
    public const int MaxNameLength = 200;

    [Required(ErrorMessage = ErrorCodes.Rights.GroupNameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Rights.GroupNameTooLong)]
    public string Name { get; set; } = string.Empty;
}

public sealed record GroupSummary(Guid Id, string Name);

/// <summary>
/// Attribue un rôle (modèle fourni ou rôle du cabinet) à un utilisateur ou à un groupe, sur le cabinet, un groupe
/// ou un dossier, avec une échéance facultative (obligatoire pour l'auditeur et les délégations).
/// </summary>
public sealed record AssignRoleRequest
{
    public SubjectKindDto SubjectKind { get; set; } = SubjectKindDto.User;

    public Guid SubjectId { get; set; }

    [Required(ErrorMessage = ErrorCodes.Rights.RoleUnknown)]
    public string Role { get; set; } = string.Empty;

    public ScopeKindDto ScopeKind { get; set; } = ScopeKindDto.Dossier;

    public Guid ScopeId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed record RoleAssignmentSummary(
    Guid Id,
    SubjectKindDto SubjectKind,
    Guid SubjectId,
    string Role,
    ScopeKindDto ScopeKind,
    Guid ScopeId,
    DateTimeOffset? ExpiresAt);
