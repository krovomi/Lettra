using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Modes 2, 3 et 5 : le registre d'un cabinet pour l'espace d'administration, lu et écrit dans la base Catalog. Comme
/// <see cref="CatalogAuthorizationAdministration"/>, tout ce qui sort d'un autre cabinet répond « introuvable ».
/// </summary>
internal sealed class CatalogAuthorizationRegistry(IDbContextFactory<CatalogDbContext> factory, TimeProvider clock) : IAuthorizationRegistry
{
    public async Task<CabinetRegistry?> ReadAsync(OrganisationId cabinet, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            Guid id = cabinet.Value;
            string? name = await db.Organisations.AsNoTracking().Where(o => o.Id == id).Select(o => o.Name).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            List<RegistryPerson> people = await db.UserAccounts.AsNoTracking()
                .Where(u => u.OrganisationId == id && u.State != AccountState.Deleted)
                .Select(u => new RegistryPerson(u.Id, u.DisplayName, u.Email, u.State, u.CreatedAt))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            List<RegistryGroup> groups = await db.Groups.AsNoTracking()
                .Where(g => g.OrganisationId == id)
                .Select(g => new RegistryGroup(g.Id, g.Name))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            HashSet<Guid> groupIds = groups.Select(g => g.Id).ToHashSet();
            HashSet<Guid> personIds = people.Select(p => p.Id).ToHashSet();

            List<RegistryMembership> memberships = (await db.GroupMembers.AsNoTracking()
                    .Where(m => groupIds.Contains(m.GroupId))
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .Where(m => personIds.Contains(m.UserId))
                .Select(m => new RegistryMembership(m.GroupId, m.UserId))
                .ToList();
            List<RegistryPlacement> placements = await db.GroupDossiers.AsNoTracking()
                .Where(p => groupIds.Contains(p.GroupId))
                .Select(p => new RegistryPlacement(p.GroupId, p.DossierId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            List<CatalogRoleAssignment> stored = await db.RoleAssignments.AsNoTracking()
                .Where(a => (a.SubjectKind == SubjectKind.User && personIds.Contains(a.SubjectId))
                    || (a.SubjectKind == SubjectKind.Group && groupIds.Contains(a.SubjectId)))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            List<RegistryAssignment> assignments = stored
                .OrderBy(a => a.Id)
                .Select(a => new RegistryAssignment(
                    a.Id,
                    new RoleAssignment(new SubjectRef(a.SubjectKind, a.SubjectId), a.RoleKey, new AuthorizationScope(a.ScopeKind, a.ScopeId), a.ExpiresAt)))
                .ToList();

            List<CatalogCustomRole> roles = await db.CustomRoles.AsNoTracking()
                .Where(r => r.OrganisationId == id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            ILookup<string, Permission> permissions = (await db.CustomRolePermissions.AsNoTracking()
                    .Where(p => p.OrganisationId == id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false))
                .ToLookup(p => p.RoleKey, p => p.Permission, StringComparer.Ordinal);
            List<RegistryCustomRole> customRoles = roles
                .Select(r => new RegistryCustomRole(new RoleDefinition(r.Key, permissions[r.Key].ToHashSet(), r.WidestScope, r.RequiresExpiry), r.Name, r.Description))
                .ToList();

            return new CabinetRegistry(cabinet, name, ReadOnly: false, people, groups, memberships, placements, assignments, customRoles);
        }
    }

    public async Task<Result<Guid>> InviteAsync(OrganisationId cabinet, string displayName, string email, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!await db.Organisations.AnyAsync(o => o.Id == cabinet.Value, cancellationToken).ConfigureAwait(false))
            {
                return DossierErrors.OrganisationNotFound;
            }

            string normalized = email.ToLowerInvariant();
            if (await EmailTakenAsync(db, cabinet.Value, normalized, except: null, cancellationToken).ConfigureAwait(false))
            {
                return AdministrationErrors.EmailTaken;
            }

            CatalogUserAccount account = new()
            {
                Id = Guid.CreateVersion7(),
                OrganisationId = cabinet.Value,
                State = AccountState.Invited,
                CreatedAt = clock.GetUtcNow(),
                DisplayName = displayName,
                Email = normalized,
            };
            db.UserAccounts.Add(account);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return account.Id;
        }
    }

    public async Task<Result> UpdatePersonAsync(OrganisationId cabinet, Guid userId, string displayName, string email, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogUserAccount? account = await db.UserAccounts
                .SingleOrDefaultAsync(u => u.Id == userId && u.OrganisationId == cabinet.Value, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                return RightsErrors.UserNotFound;
            }

            string normalized = email.ToLowerInvariant();
            if (await EmailTakenAsync(db, cabinet.Value, normalized, except: userId, cancellationToken).ConfigureAwait(false))
            {
                return AdministrationErrors.EmailTaken;
            }

            account.DisplayName = displayName;
            account.Email = normalized;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result> RenameGroupAsync(OrganisationId cabinet, GroupId group, string name, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogGroup? found = await db.Groups
                .SingleOrDefaultAsync(g => g.Id == group.Value && g.OrganisationId == cabinet.Value, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return RightsErrors.GroupNotFound;
            }

            found.Name = name;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result> DeleteGroupAsync(OrganisationId cabinet, GroupId group, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogGroup? found = await db.Groups
                .SingleOrDefaultAsync(g => g.Id == group.Value && g.OrganisationId == cabinet.Value, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return RightsErrors.GroupNotFound;
            }

            // Membres et placements suivent par cascade ; les attributions du groupe n'ont pas de clé étrangère.
            db.RoleAssignments.RemoveRange(await db.RoleAssignments
                .Where(a => a.SubjectKind == SubjectKind.Group && a.SubjectId == group.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false));
            db.RoleAssignments.RemoveRange(await db.RoleAssignments
                .Where(a => a.ScopeKind == ScopeKind.Groupe && a.ScopeId == group.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false));
            db.GroupMembers.RemoveRange(await db.GroupMembers.Where(m => m.GroupId == group.Value).ToListAsync(cancellationToken).ConfigureAwait(false));
            db.GroupDossiers.RemoveRange(await db.GroupDossiers.Where(p => p.GroupId == group.Value).ToListAsync(cancellationToken).ConfigureAwait(false));
            db.Groups.Remove(found);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result> SaveCustomRoleAsync(OrganisationId cabinet, RegistryCustomRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!await db.Organisations.AnyAsync(o => o.Id == cabinet.Value, cancellationToken).ConfigureAwait(false))
            {
                return DossierErrors.OrganisationNotFound;
            }

            string key = role.Definition.Key;
            CatalogCustomRole? stored = await db.CustomRoles
                .SingleOrDefaultAsync(r => r.OrganisationId == cabinet.Value && r.Key == key, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                stored = new CatalogCustomRole { OrganisationId = cabinet.Value, Key = key };
                db.CustomRoles.Add(stored);
            }

            stored.WidestScope = role.Definition.WidestScope;
            stored.RequiresExpiry = role.Definition.RequiresExpiry;
            stored.Name = role.Name;
            stored.Description = role.Description;

            List<CatalogCustomRolePermission> current = await db.CustomRolePermissions
                .Where(p => p.OrganisationId == cabinet.Value && p.RoleKey == key)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            db.CustomRolePermissions.RemoveRange(current.Where(p => !role.Definition.Permissions.Contains(p.Permission)));
            foreach (Permission permission in role.Definition.Permissions.Where(p => current.All(c => c.Permission != p)))
            {
                db.CustomRolePermissions.Add(new CatalogCustomRolePermission { OrganisationId = cabinet.Value, RoleKey = key, Permission = permission });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result> DeleteCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogCustomRole? stored = await db.CustomRoles
                .SingleOrDefaultAsync(r => r.OrganisationId == cabinet.Value && r.Key == key, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return AdministrationErrors.RoleNotFound;
            }

            // Une clé de rôle sur mesure est propre au cabinet (préfixe et identifiant unique) : ses attributions aussi.
            if (await db.RoleAssignments.AnyAsync(a => a.RoleKey == key, cancellationToken).ConfigureAwait(false))
            {
                return AdministrationErrors.RoleInUse;
            }

            db.CustomRoles.Remove(stored);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    private static Task<bool> EmailTakenAsync(CatalogDbContext db, Guid cabinet, string normalized, Guid? except, CancellationToken cancellationToken) =>
        db.UserAccounts.AnyAsync(
            u => u.OrganisationId == cabinet && u.Email == normalized && u.State != AccountState.Deleted && (except == null || u.Id != except),
            cancellationToken);
}
