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
    CatalogDossierFieldKeys fieldKeys,
    TenantMigrator migrator,
    TimeProvider clock,
    ILogger<CatalogDossierProvisioner> logger) : IDossierProvisioner
{
    public Task<Result<DossierId>> CreateAsync(OrganisationId organisation, string name, CancellationToken cancellationToken) =>
        CreateAsync(organisation, new DossierId(Guid.CreateVersion7()), name, cancellationToken);

    /// <summary>
    /// Un dossier sous un identifiant donné (import d'un paquet de réversibilité). Le registre garde la trace des dossiers
    /// effacés : un identifiant déjà connu ici, même effacé, est refusé.
    /// </summary>
    public async Task<Result<DossierId>> CreateAsync(OrganisationId organisation, DossierId id, string name, CancellationToken cancellationToken)
    {
        CatalogDossier dossier;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (await db.Dossiers.AnyAsync(d => d.Id == id.Value, cancellationToken).ConfigureAwait(false))
            {
                return DossierErrors.AlreadyExists;
            }

            var owner = await db.Organisations.Where(o => o.Id == organisation.Value).Select(o => new { o.CellId, o.State })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (owner is null || owner.State == OrganisationState.Departed)
            {
                return DossierErrors.OrganisationNotFound;
            }

            if (owner.State == OrganisationState.Leaving)
            {
                return PlatformErrors.CabinetLeaving;
            }

            string cellId = owner.CellId;

            CatalogCluster? cluster = await PickClusterAsync(db, cellId, cancellationToken).ConfigureAwait(false);
            if (cluster is null)
            {
                LogNoCapacity(logger, cellId);
                return DossierErrors.NoCapacity;
            }

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

    /// <summary>
    /// Retire un dossier dont l'import n'a pas abouti : sa base (vide, la copie ayant été annulée) est supprimée et son
    /// inscription retirée du registre, pour qu'un nouvel essai d'import soit possible. Un dossier déclaré n'est pas touché.
    /// </summary>
    public async Task DiscardAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogDossier? found = await db.Dossiers.SingleOrDefaultAsync(d => d.Id == dossier.Value && d.ClusterId != null, cancellationToken)
                .ConfigureAwait(false);
            if (found is null)
            {
                return;
            }

            IDatabaseAdministrator administrator = administrators.Single(a => a.Provider == found.Provider);
            string admin = await secrets.GetConnectionStringAsync(
                ClusterSecretSource.AdminRef(found.ClusterId!, found.DatabaseName!), cancellationToken).ConfigureAwait(false);
            await administrator.DropDatabaseAsync(admin, cancellationToken).ConfigureAwait(false);
            db.Dossiers.Remove(found);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            fieldKeys.Forget(dossier.Value);
        }
    }

    /// <summary>
    /// Temps au-delà duquel une création restée « en création » est tenue pour interrompue. Avant, l'API la mène encore :
    /// la reprendre en même temps appliquerait deux fois les migrations de la base neuve (colonne déjà présente).
    /// </summary>
    public static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Temps au-delà duquel la passe du Worker réessaie une création en échec. L'API a fini avec elle ; ce court délai
    /// laisse seulement à l'import d'un paquet manqué le temps de retirer son dossier, que le Worker créerait sinon vide.
    /// </summary>
    public static readonly TimeSpan FailureRetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reprend les créations interrompues (restées « en création » au-delà de <see cref="InterruptedAfter"/>) et les
    /// créations en échec : après <see cref="FailureRetryAfter"/> pour la passe du Worker (<paramref name="retryFailuresNow"/>
    /// faux), aussitôt pour l'administrateur de la plateforme.
    /// </summary>
    public async Task<int> ResumeAsync(bool retryFailuresNow, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset interrupted = now - InterruptedAfter;
        DateTimeOffset failedLongEnough = retryFailuresNow ? DateTimeOffset.MaxValue : now - FailureRetryAfter;
        List<CatalogDossier> pending;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Peu de lignes (créations en cours ou en échec) ; la date se compare ici, SQLite ne sait pas comparer un DateTimeOffset.
            pending = await db.Dossiers.AsNoTracking()
                .Where(d => (d.State == DossierState.Provisioning || d.State == DossierState.Failed) && d.ClusterId != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        pending.RemoveAll(d => d.UpdatedAt >= (d.State == DossierState.Provisioning ? interrupted : failedLongEnough));

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
            await SetStateAsync(dossier.Id, DossierState.Failed, schemaVersion: null, FailureOf(exception), CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>
    /// Ce que le catalogue garde d'un échec : le type de l'exception. Son message, qui peut nommer le serveur, l'utilisateur
    /// ou la base, ne va qu'au journal technique.
    /// </summary>
    internal static string FailureOf(Exception exception) => exception.GetType().Name;

    [LoggerMessage(Level = LogLevel.Error, Message = "Aucun serveur de bases disponible dans la cellule {CellId}")]
    private static partial void LogNoCapacity(ILogger logger, string cellId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Création de la base du dossier {DossierId} en échec ; reprise par le Worker")]
    private static partial void LogProvisioningFailed(ILogger logger, Exception exception, Guid dossierId);
}
