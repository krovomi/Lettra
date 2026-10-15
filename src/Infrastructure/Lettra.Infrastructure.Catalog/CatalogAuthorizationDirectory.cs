using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Modes 2, 3 et 5 : le registre des droits lu dans la base Catalog. Un groupe ne compte que s'il appartient au
/// cabinet de l'utilisateur (ou du dossier) ; les rôles personnalisés sont ceux du cabinet de l'utilisateur.
/// Le résultat est mis en cache par le service de permissions, invalidé sur <c>DroitsModifies</c>.
/// </summary>
internal sealed class CatalogAuthorizationDirectory(IDbContextFactory<CatalogDbContext> factory) : IAuthorizationDirectory, IScopeMemberDirectory
{
    public async ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogUserAccount? user = await db.UserAccounts.AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                return null;
            }

            List<Guid> groups = await (
                    from member in db.GroupMembers
                    join g in db.Groups on member.GroupId equals g.Id
                    where member.UserId == userId && g.OrganisationId == user.OrganisationId
                    select g.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            List<CatalogRoleAssignment> assignments = await db.RoleAssignments.AsNoTracking()
                .Where(a => (a.SubjectKind == SubjectKind.User && a.SubjectId == userId)
                    || (a.SubjectKind == SubjectKind.Group && groups.Contains(a.SubjectId)))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            List<RoleDefinition> customRoles = [];
            if (user.OrganisationId is Guid cabinet)
            {
                List<CatalogCustomRole> roles = await db.CustomRoles.AsNoTracking()
                    .Where(r => r.OrganisationId == cabinet)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                ILookup<string, Permission> permissions = (await db.CustomRolePermissions.AsNoTracking()
                        .Where(p => p.OrganisationId == cabinet)
                        .ToListAsync(cancellationToken).ConfigureAwait(false))
                    .ToLookup(p => p.RoleKey, p => p.Permission, StringComparer.Ordinal);
                customRoles.AddRange(roles.Select(r => new RoleDefinition(r.Key, permissions[r.Key].ToHashSet(), r.WidestScope, r.RequiresExpiry)));
            }

            return new UserAuthorizationProfile(
                user.Id,
                user.State,
                user.OrganisationId is Guid organisation ? new OrganisationId(organisation) : null,
                groups.Select(g => new GroupId(g)).ToArray(),
                assignments.Select(a => new RoleAssignment(
                    new SubjectRef(a.SubjectKind, a.SubjectId),
                    a.RoleKey,
                    new AuthorizationScope(a.ScopeKind, a.ScopeId),
                    a.ExpiresAt)).ToArray(),
                customRoles);
        }
    }

    public async ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken)
    {
        switch (scope.Kind)
        {
            case ScopeKind.Plateforme:
                return new ScopePath(scope, null, [AuthorizationScope.Plateforme]);

            case ScopeKind.Cabinet:
            {
                CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    bool exists = await db.Organisations.AnyAsync(o => o.Id == scope.Id, cancellationToken).ConfigureAwait(false);
                    return exists ? new ScopePath(scope, new OrganisationId(scope.Id), [scope, AuthorizationScope.Plateforme]) : null;
                }
            }

            case ScopeKind.Groupe:
            {
                CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    Guid? cabinet = await db.Groups.Where(g => g.Id == scope.Id).Select(g => (Guid?)g.OrganisationId)
                        .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    return cabinet is Guid id
                        ? new ScopePath(scope, new OrganisationId(id), [scope, AuthorizationScope.ForCabinet(new OrganisationId(id)), AuthorizationScope.Plateforme])
                        : null;
                }
            }

            case ScopeKind.Dossier:
            {
                CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    Guid? cabinet = await db.Dossiers
                        .Where(d => d.Id == scope.Id && d.State == DossierState.Active)
                        .Select(d => (Guid?)d.OrganisationId)
                        .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                    if (cabinet is not Guid organisation)
                    {
                        return null;
                    }

                    // Un groupe d'un autre cabinet ne peut pas contenir ce dossier.
                    List<Guid> groups = await (
                            from placement in db.GroupDossiers
                            join g in db.Groups on placement.GroupId equals g.Id
                            where placement.DossierId == scope.Id && g.OrganisationId == organisation
                            select g.Id)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    OrganisationId owner = new(organisation);
                    return new ScopePath(
                        scope,
                        owner,
                        [scope, .. groups.Select(g => AuthorizationScope.ForGroup(new GroupId(g))), AuthorizationScope.ForCabinet(owner), AuthorizationScope.Plateforme]);
                }
            }

            default:
                return null;
        }
    }

    public async ValueTask<IReadOnlyCollection<Guid>> ListCandidatesAsync(ScopePath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Les attributions sur l'une des portées du chemin ; le filtre fin (rôle, échéance) est celui des permissions.
            List<(SubjectKind Kind, Guid Id)> subjects = [];
            foreach (IGrouping<ScopeKind, AuthorizationScope> level in path.Ancestors.GroupBy(s => s.Kind))
            {
                Guid[] ids = [.. level.Select(s => s.Id)];
                ScopeKind kind = level.Key;
                subjects.AddRange((await db.RoleAssignments.AsNoTracking()
                        .Where(a => a.ScopeKind == kind && ids.Contains(a.ScopeId))
                        .Select(a => new { a.SubjectKind, a.SubjectId })
                        .ToListAsync(cancellationToken).ConfigureAwait(false))
                    .Select(a => (a.SubjectKind, a.SubjectId)));
            }

            Guid[] users = [.. subjects.Where(s => s.Kind == SubjectKind.User).Select(s => s.Id).Distinct()];
            Guid[] groups = [.. subjects.Where(s => s.Kind == SubjectKind.Group).Select(s => s.Id).Distinct()];

            // Un membre ne compte par son groupe que si le groupe est de son cabinet.
            List<Guid> members = await (
                    from member in db.GroupMembers
                    join g in db.Groups on member.GroupId equals g.Id
                    join account in db.UserAccounts on member.UserId equals account.Id
                    where groups.Contains(g.Id) && account.OrganisationId == g.OrganisationId
                    select member.UserId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            Guid[] candidates = [.. users.Concat(members).Distinct()];
            return await db.UserAccounts.AsNoTracking()
                .Where(u => candidates.Contains(u.Id) && u.State == AccountState.Active)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
