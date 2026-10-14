using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Lettra.Infrastructure.Catalog;

public sealed class TenantCatalog(IDbContextFactory<CatalogDbContext> factory) : ITenantCatalog
{
    public async ValueTask<TenantDescriptor?> FindAsync(Guid organisationId, Guid dossierId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var d = await db.Dossiers.AsNoTracking()
            .Where(x => x.Id == dossierId && x.OrganisationId == organisationId && x.State == DossierState.Active)
            .Select(x => new { x.Id, x.OrganisationId, x.Provider, x.ClusterId, x.DatabaseName })
            .SingleOrDefaultAsync(ct);
        return d is null ? null : ToDescriptor(d.Id, d.OrganisationId, d.Provider, d.ClusterId, d.DatabaseName);
    }

    public async ValueTask<bool> UserHasAccessAsync(string userId, Guid dossierId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Access.AnyAsync(a => a.UserId == userId && a.DossierId == dossierId, ct);
    }

    public static TenantDescriptor ToDescriptor(Dossier d) => ToDescriptor(d.Id, d.OrganisationId, d.Provider, d.ClusterId, d.DatabaseName);

    private static TenantDescriptor ToDescriptor(Guid id, Guid org, DatabaseProvider provider, string clusterId, string database) =>
        new(id, org, provider, ClusterSecretStore.AppRef(clusterId, database));
}

/// Ce que l'adaptateur d'entrée (API, MAUI) sait de l'appelant : identité (claims) et dossier choisi (en-tête X-Dossier).
public interface ICurrentRequest
{
    string? UserId { get; }
    Guid? OrganisationId { get; }
    Guid? DossierId { get; }
}

/// Modes 2, 3 et 5 : le dossier demandé est validé contre le catalogue, avec un cache court.
public sealed class CatalogTenantResolver(ICurrentRequest request, ITenantCatalog catalog, HybridCache cache) : ITenantResolver
{
    private static readonly HybridCacheEntryOptions CacheOptions = new() { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    public async ValueTask<TenantDescriptor> ResolveAsync(CancellationToken ct = default)
    {
        var userId = request.UserId ?? throw new TenantNotFoundException("Utilisateur non authentifié.");
        var orgId = request.OrganisationId ?? throw new TenantNotFoundException("Claim 'org' manquant.");
        var dossierId = request.DossierId ?? throw new TenantNotFoundException("Dossier non sélectionné.");

        var descriptor = await cache.GetOrCreateAsync(
            $"tenant:{orgId:N}:{dossierId:N}",
            (catalog, orgId, dossierId),
            static (s, token) => s.catalog.FindAsync(s.orgId, s.dossierId, token),
            CacheOptions,
            cancellationToken: ct);

        // Le droit d'accès n'est pas mis en cache : un retrait de droit s'applique à la requête suivante.
        if (descriptor is null || !await catalog.UserHasAccessAsync(userId, dossierId, ct))
            throw new TenantAccessDeniedException(dossierId);

        return descriptor;
    }
}
