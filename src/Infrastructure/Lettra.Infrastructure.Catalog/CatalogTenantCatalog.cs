using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>Modes 2, 3 et 5 : les dossiers inscrits dans la base Catalog.</summary>
internal sealed class CatalogTenantCatalog(IDbContextFactory<CatalogDbContext> factory) : ITenantCatalog, IDossierDirectory
{
    public async ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            CatalogDossier? dossier = await db.Dossiers.AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == dossierId.Value && d.State == DossierState.Active, cancellationToken)
                .ConfigureAwait(false);
            return dossier?.ToDescriptor();
        }
    }

    /// <summary>
    /// Le catalogue ne porte pas les droits : l'accès au dossier est décidé par <c>IPermissionService</c>
    /// (rôles à portée, annexe F). Ici, seul un dossier actif est ouvrable.
    /// </summary>
    public async ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
        await FindAsync(dossierId, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Dossiers.AsNoTracking()
                .Where(d => d.OrganisationId == organisation.Value && d.State == DossierState.Active)
                .OrderBy(d => d.Name)
                .Select(d => new DossierSummary(d.Id, d.Name))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Tous les dossiers actifs de la cellule, pour les passes système du Worker (contrôle d'intégrité).</summary>
    public async Task<IReadOnlyList<TenantDescriptor>> ListActiveAsync(CancellationToken cancellationToken)
    {
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<CatalogDossier> dossiers = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.Active)
                .OrderBy(d => d.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. dossiers.Select(d => d.ToDescriptor())];
        }
    }
}
