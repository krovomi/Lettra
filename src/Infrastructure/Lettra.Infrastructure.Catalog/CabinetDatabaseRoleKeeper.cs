using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Tient les rôles de connexion par cabinet (<see cref="CabinetDatabaseRoles"/>) sur les serveurs qui les activent :
/// donne chaque nouvelle base au rôle de son cabinet, fait passer les bases créées avant l'activation sur ce rôle, et
/// supprime le rôle d'un cabinet parti. Toutes ces opérations passent par la connexion d'administration du serveur.
/// </summary>
internal sealed partial class CabinetDatabaseRoleKeeper(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    CabinetDatabaseRoles roles,
    IEnumerable<ICabinetRoleAdministrator> administrators,
    ISecretStore secrets,
    IOptionsMonitor<CatalogOptions> options,
    ILogger<CabinetDatabaseRoleKeeper> logger)
{
    /// <summary>La référence de connexion d'une nouvelle base : par le rôle du cabinet si son serveur les active.</summary>
    public string ConnectionRefFor(string clusterId, string database, Guid organisation) =>
        roles.AppliesTo(clusterId)
            ? ClusterSecretSource.CabinetRef(clusterId, database, organisation)
            : ClusterSecretSource.AppRef(clusterId, database);

    /// <summary>Donne la base d'un dossier au rôle de son cabinet, quand sa référence passe par ce rôle (idempotent).</summary>
    public async Task AssignAsync(CatalogDossier dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        if (!ClusterSecretSource.IsCabinetRef(dossier.ConnectionSecretRef))
        {
            return;
        }

        DatabaseOwner owner = await roles.OwnerAsync(dossier.OrganisationId, dossier.ClusterId!, cancellationToken).ConfigureAwait(false);
        string admin = await secrets.GetConnectionStringAsync(
            ClusterSecretSource.AdminRef(dossier.ClusterId!, dossier.DatabaseName!), cancellationToken).ConfigureAwait(false);
        await Administrator(dossier.Provider).AssignOwnerAsync(admin, owner, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Bases actives créées avant l'activation des rôles sur leur serveur : chacune est donnée au rôle de son cabinet, puis
    /// sa référence change. Jusque-là elle reste ouverte par le rôle d'administration, membre du rôle du cabinet : aucune
    /// coupure. Rend le nombre de bases passées ; une base en échec est reprise à la passe suivante.
    /// </summary>
    public async Task<int> AdoptAsync(CancellationToken cancellationToken)
    {
        HashSet<string> clusters = [.. options.CurrentValue.Clusters.Keys.Where(roles.AppliesTo)];
        if (clusters.Count == 0)
        {
            return 0;
        }

        List<CatalogDossier> legacy;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            legacy = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.Active && d.ClusterId != null && clusters.Contains(d.ClusterId) && !EF.Functions.Like(d.ConnectionSecretRef, "%@%"))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        int adopted = 0;
        foreach (CatalogDossier dossier in legacy)
        {
            try
            {
                dossier.ConnectionSecretRef = ClusterSecretSource.CabinetRef(dossier.ClusterId!, dossier.DatabaseName!, dossier.OrganisationId);
                await AssignAsync(dossier, cancellationToken).ConfigureAwait(false);
                await SetRefAsync(dossier.Id, dossier.ConnectionSecretRef, cancellationToken).ConfigureAwait(false);
                adopted++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogAdoptionFailed(logger, exception, dossier.Id);
            }
        }

        return adopted;
    }

    /// <summary>Supprime le rôle d'un cabinet parti sur chaque serveur qui active les rôles (sans effet s'il n'existe pas).</summary>
    public async Task DropAsync(Guid organisation, CancellationToken cancellationToken)
    {
        foreach ((string clusterId, ClusterEntry cluster) in options.CurrentValue.Clusters.Where(c => roles.AppliesTo(c.Key)))
        {
            // La base de maintenance du serveur, toujours présente : celles du cabinet sont déjà supprimées.
            string admin = await secrets.GetConnectionStringAsync(ClusterSecretSource.AdminRef(clusterId, "postgres"), cancellationToken)
                .ConfigureAwait(false);
            await Administrator(cluster.Provider).DropOwnerAsync(admin, CabinetDatabaseRoles.RoleOf(organisation), cancellationToken).ConfigureAwait(false);
        }
    }

    private ICabinetRoleAdministrator Administrator(DatabaseProvider provider) => administrators.Single(a => a.Provider == provider);

    private async Task SetRefAsync(Guid dossierId, string secretRef, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Dossiers.Where(d => d.Id == dossierId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ConnectionSecretRef, secretRef), cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Passage de la base du dossier {DossierId} au rôle de son cabinet en échec ; repris à la passe suivante")]
    private static partial void LogAdoptionFailed(ILogger logger, Exception exception, Guid dossierId);
}
