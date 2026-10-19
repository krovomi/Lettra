using System.Collections.Frozen;
using Lettra.Application.Abstractions.Identity;

namespace Lettra.Application.Authorization;

/// <summary>
/// Calcul pur des permissions effectives (annexe F §3 et §4). Refuse par défaut : tout ce qui est
/// inconnu, expiré, incohérent ou hors du cabinet de l'utilisateur ne donne rien.
/// </summary>
public static class PermissionResolver
{
    public static readonly IReadOnlySet<Permission> None = FrozenSet<Permission>.Empty;

    public static IReadOnlySet<Permission> Resolve(UserAuthorizationProfile? profile, ScopePath? target, DateTimeOffset now)
    {
        if (profile is not { State: AccountState.Active } || target is null)
        {
            return None;
        }

        HashSet<Permission> granted = [];
        foreach (RoleAssignment assignment in profile.Assignments)
        {
            RoleDefinition? role = FindRole(profile, assignment.Role);
            if (role is not null && Applies(profile, assignment, role, target, now))
            {
                granted.UnionWith(role.Permissions);
            }
        }

        return granted.Count == 0 ? None : granted.ToFrozenSet();
    }

    private static RoleDefinition? FindRole(UserAuthorizationProfile profile, string key) =>
        RoleTemplates.All.GetValueOrDefault(key)
        ?? profile.CustomRoles.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));

    private static bool Applies(UserAuthorizationProfile profile, RoleAssignment assignment, RoleDefinition role, ScopePath target, DateTimeOffset now)
    {
        // Attribution échue, ou rôle à durée limitée attribué sans échéance (auditeur, délégation).
        if (!role.AcceptsExpiry(assignment.ExpiresAt, now))
        {
            return false;
        }

        // Le sujet doit être l'utilisateur ou l'un de ses groupes : un catalogue incohérent n'élargit rien.
        bool subjectMatches = assignment.Subject.Kind switch
        {
            SubjectKind.User => assignment.Subject.Id == profile.UserId,
            SubjectKind.Group => profile.Groups.Contains(new GroupId(assignment.Subject.Id)),
            _ => false,
        };
        if (!subjectMatches)
        {
            return false;
        }

        // Un rôle n'est jamais attribué plus largement que prévu (un collaborateur sur tout le cabinet, par exemple).
        if (!role.AllowsScope(assignment.Scope.Kind))
        {
            return false;
        }

        if (!target.Ancestors.Contains(assignment.Scope))
        {
            return false;
        }

        // Isolation des cabinets : hors plateforme, une attribution ne vaut que dans le cabinet de l'utilisateur.
        return assignment.Scope.Kind == ScopeKind.Plateforme
            || (profile.Cabinet is not null && profile.Cabinet == target.Cabinet);
    }
}
