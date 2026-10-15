using Lettra.Application.Abstractions.Identity;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Au démarrage de l'hôte : migre la base Catalog (sauf en SaaS, où le Worker le fait) puis y importe ce que la
/// configuration déclare (cabinets, serveurs de bases, dossiers existants, comptes, groupes et rôles). L'import ajoute et met à jour, il ne
/// supprime jamais : retirer un dossier du catalogue est un acte explicite.
/// </summary>
public sealed class CatalogInitializer(
    IDbContextFactory<CatalogDbContext> factory,
    IOptions<CatalogOptions> options,
    TimeProvider clock,
    IServiceScopeFactory scopes)
{
    public async Task InitializeAsync(bool migrate, CancellationToken cancellationToken)
    {
        CatalogOptions declared = options.Value;
        if (migrate)
        {
            CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Plusieurs instances peuvent démarrer ensemble sur le même catalogue : celle qui perd la course à l'insertion
        // relit ce que l'autre a écrit et recommence (l'import est idempotent).
        for (int attempt = 1; ; attempt++)
        {
            CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                try
                {
                    await ImportAsync(db, declared, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (DbUpdateException) when (attempt < MaxImportAttempts)
                {
                }
            }
        }

        // Les droits déclarés ont pu changer : les permissions en cache (partagé entre instances) sont recalculées.
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if (scope.ServiceProvider.GetService<IPermissionCacheInvalidator>() is IPermissionCacheInvalidator invalidator)
            {
                await invalidator.InvalidateAllAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private const int MaxImportAttempts = 3;

    private static async Task ImportAsync(CatalogDbContext db, CatalogOptions declared, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IEnumerable<Guid> organisationIds = declared.Organisations.Select(o => o.Id).Concat(declared.Dossiers.Select(d => d.OrganisationId)).Distinct();
        foreach (Guid id in organisationIds)
        {
            OrganisationEntry entry = declared.Organisations.FirstOrDefault(o => o.Id == id) ?? new OrganisationEntry { Id = id };
            CatalogOrganisation? organisation = await db.Organisations.FindAsync([id], cancellationToken).ConfigureAwait(false);
            if (organisation is null)
            {
                db.Organisations.Add(new CatalogOrganisation { Id = id, Name = entry.Name, CellId = entry.CellId, CreatedAt = now });
            }
            else
            {
                organisation.Name = entry.Name;
                organisation.CellId = entry.CellId;
            }
        }

        foreach ((string id, ClusterEntry entry) in declared.Clusters)
        {
            CatalogCluster? cluster = await db.Clusters.FindAsync([id], cancellationToken).ConfigureAwait(false);
            if (cluster is null)
            {
                cluster = new CatalogCluster { Id = id, CellId = entry.CellId };
                db.Clusters.Add(cluster);
            }

            cluster.CellId = entry.CellId;
            cluster.Provider = entry.Provider;
            cluster.MaxDatabases = entry.MaxDatabases;
            cluster.AcceptsNewDossiers = entry.AcceptsNewDossiers;
        }

        foreach (DossierEntry entry in declared.Dossiers)
        {
            CatalogDossier? dossier = await db.Dossiers.FindAsync([entry.Id], cancellationToken).ConfigureAwait(false);
            if (dossier is null)
            {
                dossier = new CatalogDossier
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    ConnectionSecretRef = entry.ConnectionSecretRef,
                    State = DossierState.Active,
                    CreatedAt = now,
                };
                db.Dossiers.Add(dossier);
            }

            dossier.OrganisationId = entry.OrganisationId;
            dossier.Name = entry.Name;
            dossier.Provider = entry.Provider;
            dossier.ConnectionSecretRef = entry.ConnectionSecretRef;
            dossier.Schema = entry.Schema;
            dossier.UpdatedAt = now;
        }

        await ImportAuthorizationAsync(db, declared.Authorization, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ImportAuthorizationAsync(CatalogDbContext db, AuthorizationEntries declared, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (GroupEntry entry in declared.Groups)
        {
            CatalogGroup? group = await db.Groups.FindAsync([entry.Id], cancellationToken).ConfigureAwait(false);
            if (group is null)
            {
                db.Groups.Add(new CatalogGroup { Id = entry.Id, OrganisationId = entry.Cabinet, Name = entry.Name });
            }
            else
            {
                group.OrganisationId = entry.Cabinet;
                group.Name = entry.Name;
            }

            foreach (Guid dossier in entry.Dossiers.Distinct())
            {
                if (await db.GroupDossiers.FindAsync([entry.Id, dossier], cancellationToken).ConfigureAwait(false) is null)
                {
                    db.GroupDossiers.Add(new CatalogGroupDossier { GroupId = entry.Id, DossierId = dossier });
                }
            }

            await AddAssignmentsAsync(db, SubjectKind.Group, entry.Id, entry.Assignments, cancellationToken).ConfigureAwait(false);
        }

        foreach (AccountEntry entry in declared.Users)
        {
            CatalogUserAccount? account = await db.UserAccounts.FindAsync([entry.Id], cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                db.UserAccounts.Add(new CatalogUserAccount { Id = entry.Id, OrganisationId = entry.Cabinet, State = entry.State, CreatedAt = now });
            }
            else
            {
                account.OrganisationId = entry.Cabinet;
                account.State = entry.State;
            }

            foreach (Guid group in entry.Groups.Distinct())
            {
                // Un groupe inconnu du catalogue est ignoré : l'appartenance n'a de sens que pour un groupe existant.
                if (await db.Groups.FindAsync([group], cancellationToken).ConfigureAwait(false) is not null
                    && await db.GroupMembers.FindAsync([group, entry.Id], cancellationToken).ConfigureAwait(false) is null)
                {
                    db.GroupMembers.Add(new CatalogGroupMember { GroupId = group, UserId = entry.Id });
                }
            }

            await AddAssignmentsAsync(db, SubjectKind.User, entry.Id, entry.Assignments, cancellationToken).ConfigureAwait(false);
        }

        foreach (CustomRoleEntry entry in declared.Roles)
        {
            CatalogCustomRole? role = await db.CustomRoles.FindAsync([entry.Cabinet, entry.Key], cancellationToken).ConfigureAwait(false);
            if (role is null)
            {
                role = new CatalogCustomRole { OrganisationId = entry.Cabinet, Key = entry.Key };
                db.CustomRoles.Add(role);
            }

            role.WidestScope = entry.WidestScope;
            role.RequiresExpiry = entry.RequiresExpiry;
            foreach (Permission permission in entry.Permissions.Distinct())
            {
                if (await db.CustomRolePermissions.FindAsync([entry.Cabinet, entry.Key, permission], cancellationToken).ConfigureAwait(false) is null)
                {
                    db.CustomRolePermissions.Add(new CatalogCustomRolePermission { OrganisationId = entry.Cabinet, RoleKey = entry.Key, Permission = permission });
                }
            }
        }
    }

    /// <summary>Une attribution déjà présente (même sujet, rôle et portée) est mise à jour, jamais dupliquée.</summary>
    private static async Task AddAssignmentsAsync(
        CatalogDbContext db, SubjectKind kind, Guid subject, IEnumerable<AssignmentEntry> entries, CancellationToken cancellationToken)
    {
        List<CatalogRoleAssignment> existing = await db.RoleAssignments
            .Where(a => a.SubjectKind == kind && a.SubjectId == subject)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (AssignmentEntry entry in entries)
        {
            Guid scopeId = entry.Scope == ScopeKind.Plateforme ? Guid.Empty : entry.ScopeId;
            CatalogRoleAssignment? assignment = existing.FirstOrDefault(a =>
                string.Equals(a.RoleKey, entry.Role, StringComparison.Ordinal) && a.ScopeKind == entry.Scope && a.ScopeId == scopeId);
            if (assignment is null)
            {
                assignment = new CatalogRoleAssignment
                {
                    Id = Guid.CreateVersion7(),
                    SubjectKind = kind,
                    SubjectId = subject,
                    RoleKey = entry.Role,
                    ScopeKind = entry.Scope,
                    ScopeId = scopeId,
                };
                db.RoleAssignments.Add(assignment);
                existing.Add(assignment);
            }

            assignment.ExpiresAt = entry.ExpiresAt;
        }
    }
}
