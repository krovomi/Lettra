using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Modes 2, 3 et 5 : écriture du registre des droits dans la base Catalog. Tout ce qui est relié (compte, groupe,
/// dossier, sujet d'une attribution) doit appartenir au cabinet ; sinon la réponse est « introuvable », sans dire
/// à un cabinet ce qui existe chez un autre.
/// </summary>
internal sealed class CatalogAuthorizationAdministration(IDbContextFactory<CatalogDbContext> factory, TimeProvider clock)
    : IAuthorizationAdministration
{
    public async Task<Result> SetAccountAsync(OrganisationId cabinet, Guid userId, AccountState state, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogUserAccount? account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                db.UserAccounts.Add(new CatalogUserAccount { Id = userId, OrganisationId = cabinet.Value, State = state, CreatedAt = clock.GetUtcNow() });
            }
            else if (account.OrganisationId != cabinet.Value)
            {
                return RightsErrors.UserNotFound;
            }
            else
            {
                account.State = state;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result<GroupId>> CreateGroupAsync(OrganisationId cabinet, string name, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!await db.Organisations.AnyAsync(o => o.Id == cabinet.Value, cancellationToken).ConfigureAwait(false))
            {
                return DossierErrors.OrganisationNotFound;
            }

            CatalogGroup group = new() { Id = Guid.CreateVersion7(), OrganisationId = cabinet.Value, Name = name };
            db.Groups.Add(group);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new GroupId(group.Id);
        }
    }

    public async Task<Result> SetGroupMemberAsync(GroupId group, Guid userId, bool member, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            Guid? cabinet = await GroupCabinetAsync(db, group.Value, cancellationToken).ConfigureAwait(false);
            if (cabinet is null)
            {
                return RightsErrors.GroupNotFound;
            }

            if (!await db.UserAccounts.AnyAsync(u => u.Id == userId && u.OrganisationId == cabinet, cancellationToken).ConfigureAwait(false))
            {
                return RightsErrors.UserNotFound;
            }

            CatalogGroupMember? existing = await db.GroupMembers
                .SingleOrDefaultAsync(m => m.GroupId == group.Value && m.UserId == userId, cancellationToken).ConfigureAwait(false);
            if (member && existing is null)
            {
                db.GroupMembers.Add(new CatalogGroupMember { GroupId = group.Value, UserId = userId });
            }
            else if (!member && existing is not null)
            {
                db.GroupMembers.Remove(existing);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<Result> SetDossierPlacementAsync(GroupId group, DossierId dossier, bool placed, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            Guid? cabinet = await GroupCabinetAsync(db, group.Value, cancellationToken).ConfigureAwait(false);
            if (cabinet is null)
            {
                return RightsErrors.GroupNotFound;
            }

            bool sameCabinet = await db.Dossiers
                .AnyAsync(d => d.Id == dossier.Value && d.OrganisationId == cabinet && d.State == DossierState.Active, cancellationToken)
                .ConfigureAwait(false);
            if (!sameCabinet)
            {
                return TenancyErrors.DossierNotFound;
            }

            CatalogGroupDossier? existing = await db.GroupDossiers
                .SingleOrDefaultAsync(p => p.GroupId == group.Value && p.DossierId == dossier.Value, cancellationToken).ConfigureAwait(false);
            if (placed && existing is null)
            {
                db.GroupDossiers.Add(new CatalogGroupDossier { GroupId = group.Value, DossierId = dossier.Value });
            }
            else if (!placed && existing is not null)
            {
                db.GroupDossiers.Remove(existing);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
    }

    public async Task<RoleDefinition?> FindCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogCustomRole? role = await db.CustomRoles.AsNoTracking()
                .SingleOrDefaultAsync(r => r.OrganisationId == cabinet.Value && r.Key == key, cancellationToken).ConfigureAwait(false);
            if (role is null)
            {
                return null;
            }

            List<Permission> permissions = await db.CustomRolePermissions.AsNoTracking()
                .Where(p => p.OrganisationId == cabinet.Value && p.RoleKey == key)
                .Select(p => p.Permission)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return new RoleDefinition(role.Key, permissions.ToHashSet(), role.WidestScope, role.RequiresExpiry);
        }
    }

    public async Task<Result<Guid>> AssignAsync(OrganisationId cabinet, RoleAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!await SubjectInCabinetAsync(db, assignment.Subject, cabinet.Value, cancellationToken).ConfigureAwait(false))
            {
                return RightsErrors.SubjectNotFound;
            }

            CatalogRoleAssignment? existing = await db.RoleAssignments
                .FirstOrDefaultAsync(
                    a => a.SubjectKind == assignment.Subject.Kind && a.SubjectId == assignment.Subject.Id
                        && a.RoleKey == assignment.Role && a.ScopeKind == assignment.Scope.Kind && a.ScopeId == assignment.Scope.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                existing = new CatalogRoleAssignment
                {
                    Id = Guid.CreateVersion7(),
                    SubjectKind = assignment.Subject.Kind,
                    SubjectId = assignment.Subject.Id,
                    RoleKey = assignment.Role,
                    ScopeKind = assignment.Scope.Kind,
                    ScopeId = assignment.Scope.Id,
                };
                db.RoleAssignments.Add(existing);
            }

            existing.ExpiresAt = assignment.ExpiresAt;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }
    }

    public async Task<Result<SubjectRef>> RevokeAsync(OrganisationId cabinet, Guid assignmentId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogRoleAssignment? assignment = await db.RoleAssignments
                .SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken).ConfigureAwait(false);
            SubjectRef? subject = assignment is null ? null : new SubjectRef(assignment.SubjectKind, assignment.SubjectId);
            if (subject is null || !await SubjectInCabinetAsync(db, subject.Value, cabinet.Value, cancellationToken).ConfigureAwait(false))
            {
                return RightsErrors.AssignmentNotFound;
            }

            db.RoleAssignments.Remove(assignment!);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return subject.Value;
        }
    }

    private static Task<Guid?> GroupCabinetAsync(CatalogDbContext db, Guid group, CancellationToken cancellationToken) =>
        db.Groups.Where(g => g.Id == group).Select(g => (Guid?)g.OrganisationId).SingleOrDefaultAsync(cancellationToken);

    private static Task<bool> SubjectInCabinetAsync(CatalogDbContext db, SubjectRef subject, Guid cabinet, CancellationToken cancellationToken) =>
        subject.Kind switch
        {
            SubjectKind.User => db.UserAccounts.AnyAsync(u => u.Id == subject.Id && u.OrganisationId == cabinet, cancellationToken),
            SubjectKind.Group => db.Groups.AnyAsync(g => g.Id == subject.Id && g.OrganisationId == cabinet, cancellationToken),
            _ => Task.FromResult(false),
        };
}
