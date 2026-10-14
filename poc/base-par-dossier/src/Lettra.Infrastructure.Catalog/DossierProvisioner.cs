using Lettra.Application.Abstractions.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// Crée la base d'un nouveau dossier (c'est le Worker qui crée les bases, pas l'IaC : ADR-0009).
/// Étapes idempotentes : une création interrompue reprend là où elle s'est arrêtée.
public sealed class DossierProvisioner(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IEnumerable<IDatabaseAdministrator> administrators,
    ISecretStore secrets,
    IDossierKeyring keyring,
    TenantMigrator migrator)
{
    public async Task<Dossier> ProvisionAsync(Guid organisationId, string name, string? clusterId = null, CancellationToken ct = default)
    {
        Dossier dossier;
        await using (var db = await catalogFactory.CreateDbContextAsync(ct))
        {
            var cluster = clusterId is not null
                ? await db.Clusters.SingleAsync(c => c.Id == clusterId, ct)
                : await PickClusterAsync(db, organisationId, ct);

            var id = Guid.CreateVersion7();
            dossier = new Dossier
            {
                Id = id,
                OrganisationId = organisationId,
                Name = name,
                ClusterId = cluster.Id,
                DatabaseName = $"d_{id:N}",
                Provider = cluster.Provider,
                State = DossierState.Provisioning,
                WrappedDek = await keyring.CreateWrappedDekAsync(organisationId, id, ct),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            db.Dossiers.Add(dossier);
            await db.SaveChangesAsync(ct);
        }

        await CompleteAsync(dossier, ct);
        return dossier;
    }

    /// Reprend les créations restées en cours (processus tué, base indisponible…).
    public async Task<int> ResumeAsync(CancellationToken ct = default)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(ct);
        var pending = await db.Dossiers.AsNoTracking()
            .Where(d => d.State == DossierState.Provisioning || d.State == DossierState.Failed)
            .ToListAsync(ct);
        foreach (var d in pending)
            await CompleteAsync(d, ct);
        return pending.Count;
    }

    private async Task CompleteAsync(Dossier dossier, CancellationToken ct)
    {
        try
        {
            var admin = administrators.Single(a => a.Provider == dossier.Provider);
            await admin.CreateDatabaseAsync(await secrets.GetConnectionStringAsync(ClusterSecretStore.AdminRef(dossier.ClusterId, dossier.DatabaseName), ct), ct);
            var result = await migrator.MigrateAsync(TenantCatalog.ToDescriptor(dossier), stampIdentityIfMissing: true, ct);

            await using var db = await catalogFactory.CreateDbContextAsync(ct);
            await db.Dossiers.Where(d => d.Id == dossier.Id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, DossierState.Active)
                .SetProperty(d => d.SchemaVersion, result.To)
                .SetProperty(d => d.LastError, (string?)null)
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), ct);
            dossier.State = DossierState.Active;
            dossier.SchemaVersion = result.To;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await using var db = await catalogFactory.CreateDbContextAsync(CancellationToken.None);
            await db.Dossiers.Where(d => d.Id == dossier.Id).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.State, DossierState.Failed)
                .SetProperty(d => d.LastError, Truncate(ex.Message))
                .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow), CancellationToken.None);
            throw;
        }
    }

    private static async Task<DbCluster> PickClusterAsync(CatalogDbContext db, Guid organisationId, CancellationToken ct)
    {
        var cellId = await db.Organisations.Where(o => o.Id == organisationId).Select(o => o.CellId).SingleAsync(ct);
        // Le cluster le moins rempli de la cellule du cabinet, sous sa limite de bases.
        var candidate = await db.Clusters
            .Where(c => c.CellId == cellId && c.AcceptsNewDossiers)
            .Select(c => new { Cluster = c, Count = db.Dossiers.Count(d => d.ClusterId == c.Id && d.State != DossierState.Deleted) })
            .Where(x => x.Count < x.Cluster.MaxDatabases)
            .OrderBy(x => x.Count)
            .FirstOrDefaultAsync(ct);
        return candidate?.Cluster ?? throw new InvalidOperationException($"Cellule {cellId} pleine : ajouter un cluster (PR GitOps).");
    }

    internal static string Truncate(string s) => s.Length <= 2000 ? s : s[..2000];
}
