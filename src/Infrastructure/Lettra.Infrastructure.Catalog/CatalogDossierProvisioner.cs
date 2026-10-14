using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Crée la base d'un nouveau dossier (c'est l'application qui crée les bases, pas l'IaC : ADR-0009). Le dossier est
/// d'abord inscrit « en création » avec sa DEK enveloppée, puis sa base est créée, migrée et estampillée. Chaque
/// étape est idempotente : une création interrompue est reprise par le Worker (<see cref="ResumeAsync"/>).
/// </summary>
internal sealed partial class CatalogDossierProvisioner(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IEnumerable<IDatabaseAdministrator> administrators,
    ISecretStore secrets,
    IDossierKeyring keyring,
    TenantMigrator migrator,
    TimeProvider clock,
    ILogger<CatalogDossierProvisioner> logger) : IDossierProvisioner
{
    public async Task<Result<DossierId>> CreateAsync(OrganisationId organisation, string name, CancellationToken cancellationToken)
    {
        CatalogDossier dossier;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            string? cellId = await db.Organisations.Where(o => o.Id == organisation.Value).Select(o => o.CellId)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (cellId is null)
            {
                return DossierErrors.OrganisationNotFound;
            }

            CatalogCluster? cluster = await PickClusterAsync(db, cellId, cancellationToken).ConfigureAwait(false);
            if (cluster is null)
            {
                LogNoCapacity(logger, cellId);
                return DossierErrors.NoCapacity;
            }

            DossierId id = new(Guid.CreateVersion7());
            string database = $"d_{id.Value:N}";
            DateTimeOffset now = clock.GetUtcNow();
            dossier = new CatalogDossier
            {
                Id = id.Value,
                OrganisationId = organisation.Value,
                Name = name,
                Provider = cluster.Provider,
                ClusterId = cluster.Id,
                DatabaseName = database,
                ConnectionSecretRef = ClusterSecretSource.AppRef(cluster.Id, database),
                State = DossierState.Provisioning,
                WrappedDek = await keyring.CreateWrappedDekAsync(organisation, id, cancellationToken).ConfigureAwait(false),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Dossiers.Add(dossier);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await CompleteAsync(dossier, cancellationToken).ConfigureAwait(false)
            ? new DossierId(dossier.Id)
            : DossierErrors.ProvisioningFailed;
    }

    /// <summary>Reprend les créations restées « en création » ou « en échec » (processus arrêté, serveur indisponible).</summary>
    public async Task<int> ResumeAsync(CancellationToken cancellationToken)
    {
        List<CatalogDossier> pending;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            pending = await db.Dossiers.AsNoTracking()
                .Where(d => (d.State == DossierState.Provisioning || d.State == DossierState.Failed) && d.ClusterId != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        int completed = 0;
        foreach (CatalogDossier dossier in pending)
        {
            completed += await CompleteAsync(dossier, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return completed;
    }

    private async Task<bool> CompleteAsync(CatalogDossier dossier, CancellationToken cancellationToken)
    {
        try
        {
            IDatabaseAdministrator administrator = administrators.Single(a => a.Provider == dossier.Provider);
            string admin = await secrets.GetConnectionStringAsync(
                ClusterSecretSource.AdminRef(dossier.ClusterId!, dossier.DatabaseName!), cancellationToken).ConfigureAwait(false);
            await administrator.CreateDatabaseAsync(admin, cancellationToken).ConfigureAwait(false);
            DossierMigrationResult result = await migrator.MigrateAsync(dossier.ToDescriptor(), stampIfMissing: true, cancellationToken).ConfigureAwait(false);
            await SetStateAsync(dossier.Id, DossierState.Active, result.To, error: null, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProvisioningFailed(logger, exception, dossier.Id);
            await SetStateAsync(dossier.Id, DossierState.Failed, schemaVersion: null, Truncate(exception.Message), CancellationToken.None).ConfigureAwait(false);
            return false;
        }
    }

    private async Task SetStateAsync(Guid dossierId, DossierState state, string? schemaVersion, string? error, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            DateTimeOffset now = clock.GetUtcNow();
            await db.Dossiers.Where(d => d.Id == dossierId).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.State, state)
                    .SetProperty(d => d.SchemaVersion, d => schemaVersion ?? d.SchemaVersion)
                    .SetProperty(d => d.LastError, error)
                    .SetProperty(d => d.UpdatedAt, now),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Le serveur le moins rempli de la cellule du cabinet, sous sa limite de bases.</summary>
    private static async Task<CatalogCluster?> PickClusterAsync(CatalogDbContext db, string cellId, CancellationToken cancellationToken)
    {
        var candidates = await db.Clusters.AsNoTracking()
            .Where(c => c.CellId == cellId && c.AcceptsNewDossiers)
            .Select(c => new { Cluster = c, Count = db.Dossiers.Count(d => d.ClusterId == c.Id && d.State != DossierState.Deleted) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates
            .Where(x => x.Count < x.Cluster.MaxDatabases)
            .OrderBy(x => x.Count)
            .ThenBy(x => x.Cluster.Id, StringComparer.Ordinal)
            .Select(x => x.Cluster)
            .FirstOrDefault();
    }

    internal static string Truncate(string message) => message.Length <= 2000 ? message : message[..2000];

    [LoggerMessage(Level = LogLevel.Error, Message = "Aucun serveur de bases disponible dans la cellule {CellId}")]
    private static partial void LogNoCapacity(ILogger logger, string cellId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Création de la base du dossier {DossierId} en échec ; reprise par le Worker")]
    private static partial void LogProvisioningFailed(ILogger logger, Exception exception, Guid dossierId);
}
