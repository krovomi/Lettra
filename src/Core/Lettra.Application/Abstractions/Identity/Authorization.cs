using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>Identifiant d'un groupe du cabinet (pôle, équipe, bureau).</summary>
public readonly record struct GroupId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>Niveaux de la hiérarchie (annexe F §3.1), du plus large au plus étroit.</summary>
public enum ScopeKind
{
    Plateforme = 1,
    Cabinet = 2,
    Groupe = 3,
    Dossier = 4,
}

/// <summary>Une portée : la plateforme, un cabinet, un groupe ou un dossier.</summary>
public readonly record struct AuthorizationScope(ScopeKind Kind, Guid Id)
{
    public static AuthorizationScope Plateforme => new(ScopeKind.Plateforme, Guid.Empty);

    public static AuthorizationScope ForCabinet(OrganisationId cabinet) => new(ScopeKind.Cabinet, cabinet.Value);

    public static AuthorizationScope ForGroup(GroupId group) => new(ScopeKind.Groupe, group.Value);

    public static AuthorizationScope ForDossier(DossierId dossier) => new(ScopeKind.Dossier, dossier.Value);

    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>
/// Où se situe une portée dans la hiérarchie : son cabinet et toutes les portées qui la contiennent
/// (pour un dossier : le dossier, ses groupes, son cabinet, la plateforme).
/// </summary>
public sealed record ScopePath(AuthorizationScope Target, OrganisationId? Cabinet, IReadOnlyCollection<AuthorizationScope> Ancestors);

/// <summary>États d'un compte (annexe F §3.3). Seul <see cref="Active"/> donne des droits.</summary>
public enum AccountState
{
    Invited = 1,
    Active = 2,
    Suspended = 3,
    Locked = 4,
    Disabled = 5,
    Deleted = 6,
}

/// <summary>Sujet d'une attribution : un utilisateur, ou un groupe dont il est membre.</summary>
public readonly record struct SubjectRef(SubjectKind Kind, Guid Id);

public enum SubjectKind
{
    User = 1,
    Group = 2,
}

/// <summary>
/// Une attribution relie un sujet, un rôle (modèle fourni ou rôle du cabinet) et une portée, avec une
/// échéance facultative (délégation temporaire, auditeur).
/// </summary>
public sealed record RoleAssignment(SubjectRef Subject, string Role, AuthorizationScope Scope, DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Un rôle : un ensemble de permissions, la portée la plus large à laquelle il peut être attribué, et
/// s'il exige une échéance.
/// </summary>
public sealed record RoleDefinition(string Key, IReadOnlySet<Permission> Permissions, ScopeKind WidestScope, bool RequiresExpiry = false);

/// <summary>
/// Ce que le catalogue sait d'un utilisateur pour décider de ses droits. Les attributions incluent celles
/// de ses groupes. Les rôles personnalisés sont ceux de son cabinet.
/// </summary>
public sealed record UserAuthorizationProfile(
    Guid UserId,
    AccountState State,
    OrganisationId? Cabinet,
    IReadOnlyCollection<GroupId> Groups,
    IReadOnlyCollection<RoleAssignment> Assignments,
    IReadOnlyCollection<RoleDefinition> CustomRoles);

/// <summary>
/// Port vers le registre des utilisateurs, groupes et attributions : la base Catalog en modes 2, 3 et 5,
/// le compte local en mode 1. Lecture seule ; l'administration des droits passe par ses propres cas d'usage.
/// </summary>
public interface IAuthorizationDirectory
{
    ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Situe une portée dans la hiérarchie ; <c>null</c> si elle n'existe pas.</summary>
    ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Situe d'un coup plusieurs dossiers d'un cabinet (liste des dossiers accessibles). Par défaut, un dossier à la
    /// fois ; la base Catalog le fait en une requête. Les dossiers introuvables ou d'un autre cabinet sont omis.
    /// </summary>
    async ValueTask<IReadOnlyList<ScopePath>> GetDossierPathsAsync(
        OrganisationId cabinet, IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        List<ScopePath> paths = new(dossiers.Count);
        foreach (DossierId dossier in dossiers)
        {
            ScopePath? path = await GetScopePathAsync(AuthorizationScope.ForDossier(dossier), cancellationToken).ConfigureAwait(false);
            if (path is not null && path.Cabinet == cabinet)
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}

/// <summary>
/// Qui peut être concerné par une portée : les utilisateurs actifs qui ont une attribution, directe ou par un groupe,
/// sur l'une des portées du chemin (le dossier, ses groupes, son cabinet, la plateforme). Sert au routage des alertes
/// (annexe G §6) : la liste est ensuite filtrée par <see cref="IPermissionService"/>, le même calcul que pour l'accès.
/// </summary>
public interface IScopeMemberDirectory
{
    ValueTask<IReadOnlyCollection<Guid>> ListCandidatesAsync(ScopePath path, CancellationToken cancellationToken);
}
