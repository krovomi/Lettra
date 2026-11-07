using System.Security.Cryptography;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>
/// DEK des dossiers d'un serveur (modes 2, 3 et 5), pour chiffrer leurs champs sensibles : lue enveloppée dans le
/// catalogue, ouverte par la KEK du cabinet (<see cref="IDossierKeyring"/>), gardée quelques minutes dans un cache borné
/// effacé à l'éviction. Une base déclarée sans DEK (serveur de cabinet d'avant le chiffrement des champs) garde ses champs
/// en clair ; un dossier créé par le catalogue dont la DEK manque ou ne s'ouvre plus est illisible.
/// </summary>
internal sealed class CatalogDossierFieldKeys(IDbContextFactory<CatalogDbContext> catalogFactory, IDossierKeyring keyring)
    : IDossierFieldKeys, IDisposable
{
    private readonly KeyCache<Guid> _cache = new(capacity: 4096, lifetime: TimeSpan.FromMinutes(10));

    public async ValueTask<byte[]?> FindAsync(TenantDescriptor tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (_cache.Find(tenant.DossierId.Value) is { } cached)
        {
            return cached;
        }

        CatalogDbContext db = await catalogFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var row = await db.Dossiers.AsNoTracking()
                .Where(d => d.Id == tenant.DossierId.Value)
                .Select(d => new { d.WrappedDek, d.ClusterId })
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (row is null || (row.WrappedDek.Length == 0 && row.ClusterId is null))
            {
                return null;
            }

            if (row.WrappedDek.Length == 0)
            {
                throw new CryptographicException("Clé du dossier détruite : dossier illisible.");
            }

            byte[] dek = await keyring.UnwrapDekAsync(tenant.OrganisationId, tenant.DossierId, row.WrappedDek, cancellationToken).ConfigureAwait(false);
            _cache.Set(tenant.DossierId.Value, dek);
            return dek;
        }
    }

    /// <summary>Oublie la DEK d'un dossier (effacé, clé tournée).</summary>
    public void Forget(Guid dossierId) => _cache.Remove(dossierId);

    public void Dispose() => _cache.Dispose();
}
