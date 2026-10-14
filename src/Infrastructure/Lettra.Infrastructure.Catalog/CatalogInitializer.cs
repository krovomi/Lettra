using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Au démarrage de l'hôte : migre la base Catalog (sauf en SaaS, où le Worker le fait) puis y importe ce que la
/// configuration déclare (cabinets, serveurs de bases, dossiers existants). L'import ajoute et met à jour, il ne
/// supprime jamais : retirer un dossier du catalogue est un acte explicite.
/// </summary>
public sealed class CatalogInitializer(
    IDbContextFactory<CatalogDbContext> factory,
    IOptions<CatalogOptions> options,
    TimeProvider clock)
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
                    return;
                }
                catch (DbUpdateException) when (attempt < MaxImportAttempts)
                {
                }
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

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
