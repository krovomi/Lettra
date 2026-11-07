using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// Arrivée et départ des cabinets d'un serveur, côté catalogue (plan du mode 5, étape 2). Un cabinet arrive avec sa
/// cellule et sa clé, née dans le KMS. Son départ laisse le délai de rétractation au cabinet pour exporter ses données ;
/// ensuite la passe d'effacement efface tous ses dossiers, détruit sa clé racine, supprime ses comptes chez le
/// fournisseur d'identité et au registre, et ne garde que la trace du départ (identifiant, nom, dates).
/// <para>Un cabinet déclaré dans la configuration (serveur du cabinet, démonstration) ne part pas par l'API.</para>
/// </summary>
internal sealed partial class CatalogCabinetLifecycle(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    IKekStore keys,
    IEnumerable<ICabinetKeyVault> vaults,
    CatalogDossierProvisioner provisioner,
    IIdentityProvisioning identity,
    IOptionsMonitor<CatalogOptions> options,
    TimeProvider clock,
    ILogger<CatalogCabinetLifecycle> logger) : ICabinetLifecycle
{
    /// <summary>Les clés des nouveaux cabinets naissent dans le KMS : sans KMS, ils se déclarent dans la configuration.</summary>
    private readonly ICabinetKeyVault? _vault = vaults.SingleOrDefault();

    public bool IsAvailable => _vault is not null;

    public async Task<IReadOnlyList<string>> ListCellsAsync(CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Clusters.AsNoTracking().Where(c => c.AcceptsNewDossiers).Select(c => c.CellId).Distinct().OrderBy(c => c)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PlatformCabinetResponse>> ListAsync(CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<CatalogOrganisation> organisations = await db.Organisations.AsNoTracking().OrderBy(o => o.Name)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var dossiers = await db.Dossiers.AsNoTracking()
                .Select(d => new { d.Id, d.OrganisationId, d.Name, d.State, d.LastError })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            ILookup<Guid, (Guid Id, string Name, DossierState State, string? LastError)> byCabinet =
                dossiers.ToLookup(d => d.OrganisationId, d => (d.Id, d.Name, d.State, d.LastError));
            HashSet<Guid> declared = Declared();
            return [.. organisations.Select(o => Describe(o, byCabinet[o.Id], declared.Contains(o.Id)))];
        }
    }

    public async Task<Result<PlatformCabinetResponse>> RegisterAsync(OrganisationId organisation, string name, string cellId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogOrganisation? existing = await db.Organisations.FindAsync([organisation.Value], cancellationToken).ConfigureAwait(false);
            if (existing is { State: not OrganisationState.Active })
            {
                return PlatformErrors.CabinetLeaving;
            }

            if (existing is null)
            {
                db.Organisations.Add(new CatalogOrganisation
                {
                    Id = organisation.Value,
                    Name = name,
                    CellId = cellId,
                    ManagedIdentity = true,
                    CreatedAt = clock.GetUtcNow(),
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                LogRegistered(logger, organisation.Value, cellId);
            }
        }

        // La clé du cabinet, tirée par le KMS et gardée enveloppée au catalogue (idempotent).
        _ = await keys.GetOrCreateAsync(organisation, cancellationToken).ConfigureAwait(false);
        return await DescribeAsync(organisation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PlatformCabinetResponse>> ScheduleDepartureAsync(OrganisationId organisation, Guid requestedBy, CancellationToken cancellationToken)
    {
        if (Declared().Contains(organisation.Value))
        {
            return PlatformErrors.CabinetDeclared;
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateTime due = now.UtcDateTime + options.CurrentValue.Catalog.CabinetDepartureDelay;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            OrganisationState? state = await db.Organisations.Where(o => o.Id == organisation.Value).Select(o => (OrganisationState?)o.State)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (state is null or OrganisationState.Departed)
            {
                return DossierErrors.OrganisationNotFound;
            }

            if (state == OrganisationState.Active)
            {
                await db.Organisations.Where(o => o.Id == organisation.Value && o.State == OrganisationState.Active)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(o => o.State, OrganisationState.Leaving)
                            .SetProperty(o => o.DepartureDueAt, due)
                            .SetProperty(o => o.DepartureRequestedBy, requestedBy),
                        cancellationToken).ConfigureAwait(false);
                LogDepartureScheduled(logger, organisation.Value, due);
            }
        }

        return await DescribeAsync(organisation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PlatformCabinetResponse>> CancelDepartureAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            if (!await db.Organisations.AnyAsync(o => o.Id == organisation.Value && o.State != OrganisationState.Departed, cancellationToken).ConfigureAwait(false))
            {
                return DossierErrors.OrganisationNotFound;
            }

            int reopened = await db.Organisations
                .Where(o => o.Id == organisation.Value && o.State == OrganisationState.Leaving && o.DepartureDueAt > now)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.State, OrganisationState.Active)
                        .SetProperty(o => o.DepartureDueAt, (DateTime?)null)
                        .SetProperty(o => o.DepartureRequestedBy, (Guid?)null),
                    cancellationToken).ConfigureAwait(false);
            if (reopened == 0)
            {
                return PlatformErrors.DepartureNotCancellable;
            }
        }

        return await DescribeAsync(organisation, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> RetryFailedDossiersAsync(CancellationToken cancellationToken) =>
        provisioner.ResumeAsync(retryFailuresNow: true, cancellationToken);

    /// <summary>
    /// Départs dont le délai est passé, avant l'effacement des dossiers : tous les dossiers du cabinet passent « à
    /// effacer » tout de suite (la passe d'effacement qui suit les efface). Rend les cabinets concernés.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> CloseDueDeparturesAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<Guid> due = await db.Organisations.AsNoTracking()
                .Where(o => o.State == OrganisationState.Leaving && o.DepartureDueAt <= now)
                .Select(o => o.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (due.Count > 0)
            {
                DateTimeOffset updated = clock.GetUtcNow();
                await db.Dossiers
                    .Where(d => due.Contains(d.OrganisationId) && d.ClusterId != null && d.State != DossierState.Deleted && d.State != DossierState.ErasureScheduled)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(d => d.State, DossierState.ErasureScheduled)
                            .SetProperty(d => d.ErasureDueAt, now)
                            .SetProperty(d => d.UpdatedAt, updated),
                        cancellationToken).ConfigureAwait(false);

                // Effacement prioritaire : un dossier dont l'effacement était déjà demandé n'attend plus son propre délai.
                await db.Dossiers.Where(d => due.Contains(d.OrganisationId) && d.State == DossierState.ErasureScheduled && d.ErasureDueAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.ErasureDueAt, now), cancellationToken).ConfigureAwait(false);
            }

            return due;
        }
    }

    /// <summary>
    /// Termine les départs dont tous les dossiers sont effacés : clé racine détruite dans le KMS, comptes et organisation
    /// supprimés chez le fournisseur d'identité, registre du cabinet vidé, cabinet noté « parti ». Chaque étape est
    /// idempotente ; un départ en échec (KMS ou fournisseur d'identité injoignable) est repris à la passe suivante.
    /// </summary>
    public async Task<int> CompleteDeparturesAsync(IReadOnlyList<Guid> leaving, CancellationToken cancellationToken)
    {
        int departed = 0;
        foreach (Guid cabinet in leaving)
        {
            try
            {
                departed += await CompleteAsync(new OrganisationId(cabinet), cancellationToken).ConfigureAwait(false) ? 1 : 0;
            }
            // Fournisseur d'identité ou KMS qui ne répond pas à temps : ce départ est repris, les suivants continuent.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogDepartureFailed(logger, exception, cabinet);
            }
        }

        return departed;
    }

    private async Task<bool> CompleteAsync(OrganisationId cabinet, CancellationToken cancellationToken)
    {
        bool managedIdentity;
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            int remaining = await db.Dossiers.CountAsync(d => d.OrganisationId == cabinet.Value && d.State != DossierState.Deleted, cancellationToken)
                .ConfigureAwait(false);
            if (remaining > 0)
            {
                // Les dossiers dus sont effacés juste avant, dans la même passe : un départ qui attend encore le dit.
                LogDepartureWaiting(logger, cabinet.Value, remaining);
                return false;
            }

            managedIdentity = await db.Organisations.Where(o => o.Id == cabinet.Value).Select(o => o.ManagedIdentity)
                .SingleAsync(cancellationToken).ConfigureAwait(false);
        }

        if (managedIdentity && !identity.IsConfigured)
        {
            LogIdentityMissing(logger, cabinet.Value);
            return false;
        }

        // 1. La clé racine du cabinet : sans elle, rien de ce qu'il a chiffré ne se relit, sauvegardes comprises.
        if (_vault is not null)
        {
            await _vault.DestroyAsync(cabinet, cancellationToken).ConfigureAwait(false);
        }

        // 2. Ses comptes et son organisation chez le fournisseur d'identité.
        if (managedIdentity)
        {
            await identity.DeleteOrganisationAsync(cabinet, cancellationToken).ConfigureAwait(false);
        }

        // 3. Son registre (personnes, équipes, rôles, préférences, réglages), puis la seule trace du départ.
        await ForgetRegistryAsync(cabinet.Value, cancellationToken).ConfigureAwait(false);
        LogDeparted(logger, cabinet.Value);
        return true;
    }

    private async Task ForgetRegistryAsync(Guid cabinet, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            DateTime departedAt = clock.GetUtcNow().UtcDateTime;
            List<Guid> people = await db.UserAccounts.Where(u => u.OrganisationId == cabinet).Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            List<Guid> groups = await db.Groups.Where(g => g.OrganisationId == cabinet).Select(g => g.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            List<Guid> dossiers = await db.Dossiers.Where(d => d.OrganisationId == cabinet).Select(d => d.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            await db.RoleAssignments.Where(a => people.Contains(a.SubjectId) || groups.Contains(a.SubjectId) || a.ScopeId == cabinet)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.GroupMembers.Where(m => people.Contains(m.UserId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Groups.Where(g => g.OrganisationId == cabinet).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.CustomRoles.Where(r => r.OrganisationId == cabinet).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserPreferences.Where(p => people.Contains(p.UserId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.AiSettings.Where(s => s.OrganisationId == cabinet).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OrganisationPolicies.Where(p => p.OrganisationId == cabinet).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.DossierIndicators.Where(i => dossiers.Contains(i.DossierId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.UserAccounts.Where(u => u.OrganisationId == cabinet).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Organisations.Where(o => o.Id == cabinet)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(o => o.State, OrganisationState.Departed)
                        .SetProperty(o => o.WrappedKek, (string?)null)
                        .SetProperty(o => o.DepartedAt, departedAt),
                    cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Result<PlatformCabinetResponse>> DescribeAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        PlatformCabinetResponse? cabinet = (await ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(c => c.Id == organisation.Value);
        return cabinet is null ? DossierErrors.OrganisationNotFound : cabinet;
    }

    private HashSet<Guid> Declared()
    {
        CatalogOptions current = options.CurrentValue;
        return [.. current.Organisations.Select(o => o.Id), .. current.Dossiers.Select(d => d.OrganisationId)];
    }

    private static PlatformCabinetResponse Describe(
        CatalogOrganisation organisation, IEnumerable<(Guid Id, string Name, DossierState State, string? LastError)> dossiers, bool declared)
    {
        List<(Guid Id, string Name, DossierState State, string? LastError)> all = [.. dossiers];
        return new PlatformCabinetResponse(
            organisation.Id,
            organisation.Name,
            organisation.CellId,
            organisation.State switch
            {
                OrganisationState.Leaving => PlatformCabinetStateDto.Leaving,
                OrganisationState.Departed => PlatformCabinetStateDto.Departed,
                _ => PlatformCabinetStateDto.Active,
            },
            declared,
            all.Count(d => d.State == DossierState.Active),
            all.Count(d => d.State == DossierState.ErasureScheduled),
            [.. all.Where(d => d.State == DossierState.Failed).OrderBy(d => d.Name).Select(d => new PlatformDossierIssue(d.Id, d.Name, d.LastError))],
            organisation.CreatedAt,
            Utc(organisation.DepartureDueAt),
            Utc(organisation.DepartedAt));
    }

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is DateTime at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Cabinet {OrganisationId} inscrit dans la cellule {CellId}")]
    private static partial void LogRegistered(ILogger logger, Guid organisationId, string cellId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Départ du cabinet {OrganisationId} demandé, effectif le {DueAt:O}")]
    private static partial void LogDepartureScheduled(ILogger logger, Guid organisationId, DateTime dueAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cabinet {OrganisationId} parti : dossiers effacés, clé détruite, comptes supprimés")]
    private static partial void LogDeparted(ILogger logger, Guid organisationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Départ du cabinet {OrganisationId} en échec, repris à la prochaine passe")]
    private static partial void LogDepartureFailed(ILogger logger, Exception exception, Guid organisationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Départ du cabinet {OrganisationId} en attente : {Remaining} dossier(s) pas encore effacé(s)")]
    private static partial void LogDepartureWaiting(ILogger logger, Guid organisationId, int remaining);

    [LoggerMessage(Level = LogLevel.Error, Message = "Départ du cabinet {OrganisationId} en attente : fournisseur d'identité de la plateforme non réglé sur cet hôte")]
    private static partial void LogIdentityMissing(ILogger logger, Guid organisationId);
}

/// <summary>
/// La passe d'effacement complète : départs de cabinets arrivés à échéance (leurs dossiers passent « à effacer »),
/// effacement des dossiers dus, puis fin des départs dont tous les dossiers sont effacés.
/// </summary>
internal sealed class CatalogErasurePass(CatalogCabinetLifecycle cabinets, CatalogDossierEraser dossiers) : IDossierErasurePass
{
    public async Task<int> EraseDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> leaving = await cabinets.CloseDueDeparturesAsync(cancellationToken).ConfigureAwait(false);
        int erased = await dossiers.EraseDueAsync(cancellationToken).ConfigureAwait(false);
        await cabinets.CompleteDeparturesAsync(leaving, cancellationToken).ConfigureAwait(false);
        return erased;
    }
}
