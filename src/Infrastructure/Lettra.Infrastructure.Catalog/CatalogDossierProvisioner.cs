using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Crée la base d'un nouveau dossier (c'est l'application qui crée les bases, pas l'IaC : ADR-0009). Le dossier est
/// d'abord inscrit « en création » avec sa DEK enveloppée, puis sa base est créée, migrée et estampillée. Chaque
/// étape est idempotente : une création interrompue est reprise par le Worker (<see cref="ResumeAsync"/>). Sur un serveur
/// qui active les rôles par cabinet, la base est donnée au rôle de son cabinet avant d'être migrée. Sur une cellule qui
/// laisse l'administration des bases au Worker (<see cref="DatabaseAdministration.Worker"/>), l'API inscrit seulement la
/// demande et attend : le Worker la prend (<see cref="RunRequestsAsync"/>), crée la base et la migre.
/// </summary>
internal sealed partial class CatalogDossierProvisioner(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IEnumerable<IDatabaseAdministrator> administrators,
    ISecretStore secrets,
    IDossierKeyring keyring,
    CatalogDossierFieldKeys fieldKeys,
    IOptionsMonitor<CatalogOptions> options,
    TenantMigrator migrator,
    CabinetDatabaseRoleKeeper cabinetRoles,
    TimeProvider clock,
    ILogger<CatalogDossierProvisioner> logger) : IDossierProvisioner, IDossierRequestPass
{
    /// <summary>Intervalle entre deux lectures de l'état d'une création menée par le Worker.</summary>
    private static readonly TimeSpan WorkerPoll = TimeSpan.FromMilliseconds(250);

    private bool ByWorker => options.CurrentValue.Catalog.DatabaseAdministration == DatabaseAdministration.Worker;

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

            int held = await db.Dossiers.CountAsync(d => d.OrganisationId == organisation.Value && d.State != DossierState.Deleted, cancellationToken)
                .ConfigureAwait(false);
            if (held >= options.CurrentValue.Catalog.MaxDossiersPerCabinet)
            {
                return DossierErrors.QuotaReached;
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
                ConnectionSecretRef = cabinetRoles.ConnectionRefFor(cluster.Id, database, organisation.Value),
                State = ByWorker ? DossierState.Requested : DossierState.Provisioning,
                WrappedDek = await keyring.CreateWrappedDekAsync(organisation, id, cancellationToken).ConfigureAwait(false),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Dossiers.Add(dossier);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        bool created = ByWorker
            ? await AwaitWorkerAsync(dossier.Id, cancellationToken).ConfigureAwait(false)
            : await CompleteAsync(dossier, cancellationToken).ConfigureAwait(false);
        return created
            ? new DossierId(dossier.Id)
            : DossierErrors.ProvisioningFailed;
    }

    /// <summary>
    /// Retire un dossier dont l'import n'a pas abouti : sa base (vide, la copie ayant été annulée) est supprimée et son
    /// inscription retirée du registre, pour qu'un nouvel essai d'import soit possible. Un dossier déclaré n'est pas touché.
    /// Quand le Worker administre les bases, le dossier est seulement marqué « à retirer » : le Worker fait le reste.
    /// </summary>
    public async Task DiscardAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        fieldKeys.Forget(dossier.Value);
        CatalogDossier? found;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            found = await db.Dossiers.AsNoTracking().SingleOrDefaultAsync(d => d.Id == dossier.Value && d.ClusterId != null, cancellationToken)
                .ConfigureAwait(false);
            if (found is not null && ByWorker)
            {
                DateTimeOffset now = clock.GetUtcNow();
                await db.Dossiers.Where(d => d.Id == dossier.Value).ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.State, DossierState.Discarding).SetProperty(d => d.UpdatedAt, now), cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (found is not null)
        {
            await DropAndRemoveAsync(found, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// La passe rapide du Worker quand il administre seul les bases : chaque création demandée est prise (une seule fois,
    /// même avec plusieurs Workers), menée jusqu'au bout, et chaque import manqué est retiré. Rend le nombre de demandes menées.
    /// </summary>
    public async Task<int> RunRequestsAsync(CancellationToken cancellationToken)
    {
        List<CatalogDossier> pending;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            pending = await db.Dossiers.AsNoTracking()
                .Where(d => (d.State == DossierState.Requested || d.State == DossierState.Discarding) && d.ClusterId != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        int done = 0;
        foreach (CatalogDossier dossier in pending.OrderBy(d => d.UpdatedAt))
        {
            if (dossier.State == DossierState.Discarding)
            {
                done += await TryDiscardAsync(dossier, cancellationToken).ConfigureAwait(false) ? 1 : 0;
            }
            else if (await ClaimAsync(dossier.Id, cancellationToken).ConfigureAwait(false))
            {
                await CompleteAsync(dossier, cancellationToken).ConfigureAwait(false);
                done++;
            }
        }

        return done;
    }

    /// <summary>
    /// Réessai immédiat des créations en échec, demandé par l'administrateur de la plateforme : mené ici, ou rendu au Worker
    /// quand c'est lui qui administre les bases. Rend le nombre de créations menées ou rendues.
    /// </summary>
    public async Task<int> RetryFailuresNowAsync(CancellationToken cancellationToken)
    {
        if (!ByWorker)
        {
            return await ResumeAsync(retryFailuresNow: true, cancellationToken).ConfigureAwait(false);
        }

        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            DateTimeOffset now = clock.GetUtcNow();
            return await db.Dossiers.Where(d => d.State == DossierState.Failed && d.ClusterId != null).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.State, DossierState.Requested).SetProperty(d => d.UpdatedAt, now), cancellationToken).ConfigureAwait(false);
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
            // Avant les migrations : les tables naissent alors au rôle du cabinet, qui seul ouvre la base.
            await cabinetRoles.AssignAsync(dossier, cancellationToken).ConfigureAwait(false);
            DossierMigrationResult result = await migrator.MigrateAsync(dossier.ToDescriptor(), stampIfMissing: true, cancellationToken).ConfigureAwait(false);
            // Un import manqué entre-temps (dossier « à retirer ») ne revient pas : l'état ne change que d'une création en cours.
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

    /// <summary>
    /// Attend qu'une création demandée au Worker aboutisse (dossier actif) ou échoue. Au-delà de
    /// <see cref="CatalogDatabaseOptions.ProvisioningWait"/>, la demande reste au Worker et l'appel rend un échec.
    /// </summary>
    private async Task<bool> AwaitWorkerAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = new(options.CurrentValue.Catalog.ProvisioningWait, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
        try
        {
            while (true)
            {
                await Task.Delay(WorkerPoll, clock, linked.Token).ConfigureAwait(false);
                CatalogDbContext db = await catalogFactory.CreateDbContextAsync(linked.Token).ConfigureAwait(false);
                await using (db.ConfigureAwait(false))
                {
                    DossierState? state = await db.Dossiers.Where(d => d.Id == dossierId).Select(d => (DossierState?)d.State)
                        .SingleOrDefaultAsync(linked.Token).ConfigureAwait(false);
                    if (state is not (DossierState.Requested or DossierState.Provisioning))
                    {
                        return state == DossierState.Active;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogStillRequested(logger, dossierId);
            return false;
        }
    }

    /// <summary>Prend une demande de création : vrai pour le seul Worker qui l'a fait passer de « demandée » à « en création ».</summary>
    private async Task<bool> ClaimAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            DateTimeOffset now = clock.GetUtcNow();
            return await db.Dossiers.Where(d => d.Id == dossierId && d.State == DossierState.Requested).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.State, DossierState.Provisioning).SetProperty(d => d.UpdatedAt, now), cancellationToken).ConfigureAwait(false) == 1;
        }
    }

    private async Task<bool> TryDiscardAsync(CatalogDossier dossier, CancellationToken cancellationToken)
    {
        try
        {
            await DropAndRemoveAsync(dossier, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDiscardFailed(logger, exception, dossier.Id);
            return false;
        }
    }

    /// <summary>Supprime la base d'un dossier par la connexion d'administration de son serveur, puis son inscription.</summary>
    private async Task DropAndRemoveAsync(CatalogDossier dossier, CancellationToken cancellationToken)
    {
        IDatabaseAdministrator administrator = administrators.Single(a => a.Provider == dossier.Provider);
        string admin = await secrets.GetConnectionStringAsync(
            ClusterSecretSource.AdminRef(dossier.ClusterId!, dossier.DatabaseName!), cancellationToken).ConfigureAwait(false);
        await administrator.DropDatabaseAsync(admin, cancellationToken).ConfigureAwait(false);
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Dossiers.Where(d => d.Id == dossier.Id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        fieldKeys.Forget(dossier.Id);
    }

    private async Task SetStateAsync(Guid dossierId, DossierState state, string? schemaVersion, string? error, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            DateTimeOffset now = clock.GetUtcNow();
            await db.Dossiers.Where(d => d.Id == dossierId && (d.State == DossierState.Provisioning || d.State == DossierState.Failed)).ExecuteUpdateAsync(
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Création de la base du dossier {DossierId} pas encore menée par le Worker ; la demande lui reste")]
    private static partial void LogStillRequested(ILogger logger, Guid dossierId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retrait de la base du dossier {DossierId} en échec ; repris à la passe suivante")]
    private static partial void LogDiscardFailed(ILogger logger, Exception exception, Guid dossierId);
}
