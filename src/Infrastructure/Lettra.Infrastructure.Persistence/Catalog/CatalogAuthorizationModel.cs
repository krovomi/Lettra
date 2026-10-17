using Lettra.Application.Abstractions.Identity;

namespace Lettra.Infrastructure.Persistence.Catalog;

/// <summary>
/// Un compte : l'identifiant est le <c>sub</c> Keycloak. Seul l'état <see cref="AccountState.Active"/> donne des droits.
/// Aucune donnée personnelle ici : le nom et l'adresse restent dans Keycloak.
/// </summary>
public sealed class CatalogUserAccount
{
    public Guid Id { get; set; }

    /// <summary><c>null</c> pour un compte de la plateforme (support, exploitation).</summary>
    public Guid? OrganisationId { get; set; }

    public AccountState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public const int MaxDisplayNameLength = 200;
    public const int MaxEmailLength = 254;

    /// <summary>Nom affiché dans l'espace d'administration ; jamais utilisé pour décider d'un droit.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Adresse de la personne, unique dans son cabinet (comparée en minuscules).</summary>
    public string? Email { get; set; }
}

/// <summary>Un groupe du cabinet (pôle, équipe, bureau).</summary>
public sealed class CatalogGroup
{
    public const int MaxNameLength = 200;

    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public required string Name { get; set; }
}

public sealed class CatalogGroupMember
{
    public Guid GroupId { get; set; }

    public Guid UserId { get; set; }
}

/// <summary>Placement d'un dossier dans un groupe (0 à n groupes par dossier).</summary>
public sealed class CatalogGroupDossier
{
    public Guid GroupId { get; set; }

    public Guid DossierId { get; set; }
}

/// <summary>Un rôle attribué à un utilisateur ou à un groupe, sur une portée, avec une échéance facultative.</summary>
public sealed class CatalogRoleAssignment
{
    public const int MaxRoleKeyLength = 64;

    public Guid Id { get; set; }

    public SubjectKind SubjectKind { get; set; }

    public Guid SubjectId { get; set; }

    public required string RoleKey { get; set; }

    public ScopeKind ScopeKind { get; set; }

    /// <summary><see cref="Guid.Empty"/> pour la plateforme.</summary>
    public Guid ScopeId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Rôle composé par un cabinet à partir des permissions fournies.</summary>
public sealed class CatalogCustomRole
{
    public Guid OrganisationId { get; set; }

    public required string Key { get; set; }

    public ScopeKind WidestScope { get; set; }

    public bool RequiresExpiry { get; set; }

    public const int MaxNameLength = 80;
    public const int MaxDescriptionLength = 300;

    /// <summary>Nom donné par l'administrateur (espace d'administration).</summary>
    public string? Name { get; set; }

    public string? Description { get; set; }
}

public sealed class CatalogCustomRolePermission
{
    public Guid OrganisationId { get; set; }

    public required string RoleKey { get; set; }

    public Permission Permission { get; set; }
}
