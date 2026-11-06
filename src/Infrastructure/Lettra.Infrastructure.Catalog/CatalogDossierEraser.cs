using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Effacement des dossiers d'un serveur (plan du mode 5, étape 2). La demande ferme le dossier aussitôt (il n'est plus
/// « actif », donc plus ouvrable ni listé) et reste annulable pendant le délai de rétractation. Ensuite, la passe
/// d'effacement détruit d'abord la DEK (le dossier devient illisible, sauvegardes comprises), puis supprime la base, puis
/// note le dossier « effacé ». Chaque étape est idempotente : une passe interrompue est reprise à la suivante.
/// <para>
/// Seules les bases créées par le catalogue s'effacent ainsi ; une base déclarée dans la configuration (serveur du
/// cabinet, démonstration) appartient à qui l'a déclarée.
/// </para>
/// </summary>
internal sealed partial class CatalogDossierEraser(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IEnumerable<IDatabaseAdministrator> administrators,
    ISecretStore secrets,
    CatalogDossierFieldKeys fieldKeys,
    IOptionsMonitor<CatalogOptions> options,
    TimeProvider clock,
    ILogger<CatalogDossierEraser> logger) : IDossierEraser, IDossierErasurePass
{
    public async Task<Result<DossierErasureResponse>> ScheduleAsync(
        OrganisationId organisation, DossierId dossier, Guid requestedBy, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var found = await db.Dossiers.AsNoTracking()
                .Where(d => d.Id == dossier.Value && d.OrganisationId == organisation.Value && d.State == DossierState.Active)
                .Select(d => new { d.Name, d.ClusterId })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return TenancyErrors.DossierNotFound;
            }

            if (found.ClusterId is null)
            {
                return DossierErrors.Declared;
            }

            DateTimeOffset now = clock.GetUtcNow();
            DateTime due = now.UtcDateTime + options.CurrentValue.Catalog.DossierErasureDelay;
            int scheduled = await db.Dossiers.Where(d => d.Id == dossier.Value && d.State == DossierState.Active)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.State, DossierState.ErasureScheduled)
                        .SetProperty(d => d.ErasureDueAt, due)
                        .SetProperty(d => d.ErasureRequestedBy, requestedBy)
                        .SetProperty(d => d.UpdatedAt, now),
                    cancellationToken).ConfigureAwait(false);
            if (scheduled == 0)
            {
                return TenancyErrors.DossierNotFound;
            }

            LogScheduled(logger, dossier.Value, due);
            return new DossierErasureResponse(dossier.Value, found.Name, new DateTimeOffset(due, TimeSpan.Zero));
        }
    }

    public async Task<Result<DossierSummary>> CancelAsync(OrganisationId organisation, DossierId dossier, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var found = await db.Dossiers.AsNoTracking()
                .Where(d => d.Id == dossier.Value && d.OrganisationId == organisation.Value)
                .Select(d => new { d.Name, d.State })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (found is null || found.State is DossierState.Deleted)
            {
                return TenancyErrors.DossierNotFound;
            }

            DateTimeOffset now = clock.GetUtcNow();
            DateTime current = now.UtcDateTime;
            int reopened = await db.Dossiers
                .Where(d => d.Id == dossier.Value && d.State == DossierState.ErasureScheduled && d.ErasureDueAt > current)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.State, DossierState.Active)
                        .SetProperty(d => d.ErasureDueAt, (DateTime?)null)
                        .SetProperty(d => d.ErasureRequestedBy, (Guid?)null)
                        .SetProperty(d => d.UpdatedAt, now),
                    cancellationToken).ConfigureAwait(false);
            return reopened == 1 ? new DossierSummary(dossier.Value, found.Name) : DossierErrors.ErasureNotCancellable;
        }
    }

    public async Task<IReadOnlyList<DossierErasureResponse>> ListScheduledAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = await db.Dossiers.AsNoTracking()
                .Where(d => d.OrganisationId == organisation.Value && d.State == DossierState.ErasureScheduled)
                .OrderBy(d => d.ErasureDueAt)
                .Select(d => new { d.Id, d.Name, d.ErasureDueAt })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. rows.Select(r => new DossierErasureResponse(r.Id, r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.ErasureDueAt!.Value, DateTimeKind.Utc))))];
        }
    }

    /// <summary>
    /// Efface les dossiers dont le délai de rétractation est passé : DEK détruite, base supprimée, dossier « effacé ».
    /// Rend le nombre de dossiers effacés ; un dossier en échec (serveur de bases injoignable) est repris à la passe suivante.
    /// </summary>
    public async Task<int> EraseDueAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        List<CatalogDossier> due;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            due = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.ErasureScheduled && d.ErasureDueAt <= now && d.ClusterId != null)
                .OrderBy(d => d.ErasureDueAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        int erased = 0;
        foreach (CatalogDossier dossier in due)
        {
            erased += await EraseAsync(dossier, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        return erased;
    }

    private async Task<bool> EraseAsync(CatalogDossier dossier, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Effacement cryptographique : sans DEK, les champs chiffrés sont illisibles, dans la base et ses sauvegardes.
            await UpdateAsync(dossier.Id, s => s.SetProperty(d => d.WrappedDek, []), cancellationToken).ConfigureAwait(false);
            fieldKeys.Forget(dossier.Id);

            // 2. La base elle-même, par la connexion d'administration du serveur (sans erreur si elle n'existe déjà plus).
            IDatabaseAdministrator administrator = administrators.Single(a => a.Provider == dossier.Provider);
            string admin = await secrets.GetConnectionStringAsync(
                ClusterSecretSource.AdminRef(dossier.ClusterId!, dossier.DatabaseName!), cancellationToken).ConfigureAwait(false);
            await administrator.DropDatabaseAsync(admin, cancellationToken).ConfigureAwait(false);

            // 3. Le registre garde la trace de l'effacement (identifiant, nom, dates), sans aucune donnée du dossier.
            DateTimeOffset now = clock.GetUtcNow();
            await UpdateAsync(
                dossier.Id,
                s => s.SetProperty(d => d.State, DossierState.Deleted)
                    .SetProperty(d => d.ErasedAt, now.UtcDateTime)
                    .SetProperty(d => d.LastError, (string?)null)
                    .SetProperty(d => d.UpdatedAt, now),
                cancellationToken).ConfigureAwait(false);
            LogErased(logger, dossier.Id);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogErasureFailed(logger, exception, dossier.Id);
            await UpdateAsync(dossier.Id, s => s.SetProperty(d => d.LastError, exception.GetType().Name), CancellationToken.None).ConfigureAwait(false);
            return false;
        }
    }

    private async Task UpdateAsync(
        Guid dossierId,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<CatalogDossier>> setters,
        CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            await db.Dossiers.Where(d => d.Id == dossierId && d.State == DossierState.ErasureScheduled)
                .ExecuteUpdateAsync(setters, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Effacement du dossier {DossierId} demandé, effectif le {DueAt:O}")]
    private static partial void LogScheduled(ILogger logger, Guid dossierId, DateTime dueAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dossier {DossierId} effacé : clé détruite, base supprimée")]
    private static partial void LogErased(ILogger logger, Guid dossierId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Effacement du dossier {DossierId} en échec, repris à la prochaine passe")]
    private static partial void LogErasureFailed(ILogger logger, Exception exception, Guid dossierId);
}
