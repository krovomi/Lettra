using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Oidc;

/// <summary>Une attribution telle qu'elle est écrite en configuration.</summary>
public sealed class AssignmentEntry
{
    public string Role { get; set; } = string.Empty;

    public ScopeKind Scope { get; set; } = ScopeKind.Dossier;

    /// <summary>Identifiant du cabinet, du groupe ou du dossier (ignoré pour la plateforme).</summary>
    public Guid ScopeId { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class UserEntry
{
    public Guid Id { get; set; }

    public AccountState State { get; set; } = AccountState.Active;

    public Guid? Cabinet { get; set; }

    public IList<Guid> Groups { get; init; } = [];

    public IList<AssignmentEntry> Assignments { get; init; } = [];
}

public sealed class GroupEntry
{
    public Guid Id { get; set; }

    public Guid Cabinet { get; set; }

    public IList<Guid> Dossiers { get; init; } = [];

    public IList<AssignmentEntry> Assignments { get; init; } = [];
}

/// <summary>Rôle composé par un cabinet à partir des permissions fournies.</summary>
public sealed class CustomRoleEntry
{
    public Guid Cabinet { get; set; }

    public string Key { get; set; } = string.Empty;

    public IList<Permission> Permissions { get; init; } = [];

    public ScopeKind WidestScope { get; set; } = ScopeKind.Dossier;

    public bool RequiresExpiry { get; set; }
}

/// <summary>Section <c>Lettra:Authorization</c>.</summary>
public sealed class AuthorizationDirectoryOptions
{
    public const string SectionName = "Lettra:Authorization";

    public IList<UserEntry> Users { get; init; } = [];

    public IList<GroupEntry> Groups { get; init; } = [];

    public IList<CustomRoleEntry> Roles { get; init; } = [];
}

/// <summary>
/// Registre des droits lu dans la configuration : serveur de cabinet de démonstration (mode 2) et tests, en
/// attendant la base Catalog qui implémentera le même port. Le cabinet d'un dossier vient du catalogue des dossiers.
/// </summary>
internal sealed class ConfigurationAuthorizationDirectory(
    IOptionsMonitor<AuthorizationDirectoryOptions> options,
    ITenantCatalog catalog) : IAuthorizationDirectory
{
    public ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        AuthorizationDirectoryOptions current = options.CurrentValue;
        UserEntry? user = current.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return ValueTask.FromResult<UserAuthorizationProfile?>(null);
        }

        // Un groupe n'est pris en compte que s'il appartient au cabinet de l'utilisateur.
        GroupEntry[] groups = current.Groups.Where(g => user.Groups.Contains(g.Id) && g.Cabinet == user.Cabinet).ToArray();
        List<RoleAssignment> assignments =
        [
            .. user.Assignments.Select(a => ToAssignment(new SubjectRef(SubjectKind.User, user.Id), a)),
            .. groups.SelectMany(g => g.Assignments.Select(a => ToAssignment(new SubjectRef(SubjectKind.Group, g.Id), a))),
        ];
        RoleDefinition[] customRoles = current.Roles
            .Where(r => r.Cabinet == user.Cabinet)
            .Select(r => new RoleDefinition(r.Key, r.Permissions.ToHashSet(), r.WidestScope, r.RequiresExpiry))
            .ToArray();

        return ValueTask.FromResult<UserAuthorizationProfile?>(new UserAuthorizationProfile(
            user.Id,
            user.State,
            user.Cabinet is Guid cabinet ? new OrganisationId(cabinet) : null,
            groups.Select(g => new GroupId(g.Id)).ToArray(),
            assignments,
            customRoles));
    }

    public async ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken)
    {
        AuthorizationDirectoryOptions current = options.CurrentValue;
        switch (scope.Kind)
        {
            case ScopeKind.Plateforme:
                return new ScopePath(scope, null, [AuthorizationScope.Plateforme]);

            case ScopeKind.Cabinet:
                return new ScopePath(scope, new OrganisationId(scope.Id), [scope, AuthorizationScope.Plateforme]);

            case ScopeKind.Groupe:
                GroupEntry? group = current.Groups.FirstOrDefault(g => g.Id == scope.Id);
                return group is null
                    ? null
                    : new ScopePath(scope, new OrganisationId(group.Cabinet), [scope, AuthorizationScope.ForCabinet(new OrganisationId(group.Cabinet)), AuthorizationScope.Plateforme]);

            case ScopeKind.Dossier:
                TenantDescriptor? dossier = await catalog.FindAsync(new DossierId(scope.Id), cancellationToken).ConfigureAwait(false);
                if (dossier is null)
                {
                    return null;
                }

                // Un groupe d'un autre cabinet ne peut pas contenir ce dossier.
                IEnumerable<AuthorizationScope> groups = current.Groups
                    .Where(g => g.Cabinet == dossier.OrganisationId.Value && g.Dossiers.Contains(scope.Id))
                    .Select(g => AuthorizationScope.ForGroup(new GroupId(g.Id)));
                return new ScopePath(
                    scope,
                    dossier.OrganisationId,
                    [scope, .. groups, AuthorizationScope.ForCabinet(dossier.OrganisationId), AuthorizationScope.Plateforme]);

            default:
                return null;
        }
    }

    private static RoleAssignment ToAssignment(SubjectRef subject, AssignmentEntry entry) =>
        new(subject, entry.Role, new AuthorizationScope(entry.Scope, entry.Scope == ScopeKind.Plateforme ? Guid.Empty : entry.ScopeId), entry.ExpiresAt);
}
