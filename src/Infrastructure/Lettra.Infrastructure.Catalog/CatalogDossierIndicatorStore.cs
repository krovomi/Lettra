using Lettra.Application.Abstractions.Cabinet;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Catalog;

/// <summary>Indicateurs de la vue cabinet dans la base Catalog : une lecture pour tous les dossiers de l'écran.</summary>
internal sealed class CatalogDossierIndicatorStore(IDbContextFactory<CatalogDbContext> factory) : IDossierIndicatorStore
{
    public async Task ReplaceAsync(DossierId dossier, IReadOnlyList<DossierIndicator> indicators, DateTimeOffset computedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indicators);
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<CatalogDossierIndicator> existing = await db.DossierIndicators.AsTracking()
                .Where(i => i.DossierId == dossier.Value)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            // Une clé rendue deux fois par des sources différentes : la dernière l'emporte.
            Dictionary<string, DossierIndicator> wanted = indicators
                .Where(i => i.Key.Length is > 0 and <= CatalogDossierIndicator.MaxKeyLength)
                .GroupBy(i => i.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            foreach (CatalogDossierIndicator row in existing.Where(r => !wanted.ContainsKey(r.Key)))
            {
                db.DossierIndicators.Remove(row);
            }

            foreach (DossierIndicator indicator in wanted.Values)
            {
                CatalogDossierIndicator? row = existing.FirstOrDefault(e => e.Key == indicator.Key);
                if (row is null)
                {
                    row = new CatalogDossierIndicator { DossierId = dossier.Value, Key = indicator.Key };
                    db.DossierIndicators.Add(row);
                }

                row.Count = indicator.Count;
                row.Level = (int)indicator.Level;
                row.DueOn = indicator.DueOn;
                row.ComputedAt = computedAt;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<StoredDossierIndicators>> ReadAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        if (dossiers.Count == 0)
        {
            return [];
        }

        Guid[] ids = [.. dossiers.Select(d => d.Value)];
        CatalogDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            List<CatalogDossierIndicator> rows = await db.DossierIndicators.AsNoTracking()
                .Where(i => ids.Contains(i.DossierId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return
            [
                .. rows.GroupBy(r => r.DossierId).Select(g => new StoredDossierIndicators(
                    new DossierId(g.Key),
                    g.Max(r => r.ComputedAt),
                    [.. g.Select(r => new DossierIndicator(r.Key, r.Count, (IndicatorLevel)r.Level, r.DueOn))])),
            ];
        }
    }
}
