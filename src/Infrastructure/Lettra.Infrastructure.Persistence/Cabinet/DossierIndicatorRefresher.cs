using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Persistence.Cabinet;

/// <summary>
/// Ouvre chaque dossier désigné dans sa propre portée (comme les passes de nuit du Worker), y fait tourner toutes les
/// <see cref="IDossierIndicatorSource"/> et range le résultat. Une source ou un dossier en échec n'arrête pas les autres ;
/// un dossier en échec garde ses anciennes valeurs. Aucune donnée de dossier n'est journalisée.
/// </summary>
internal sealed partial class DossierIndicatorRefresher(
    IServiceScopeFactory scopes,
    ITenantCatalog catalog,
    IDossierIndicatorStore store,
    TimeProvider clock,
    ILogger<DossierIndicatorRefresher> logger) : IDossierIndicatorRefresher
{
    public async Task<IndicatorRefreshOutcome> RefreshAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        int refreshed = 0;
        int failed = 0;
        foreach (DossierId dossier in dossiers)
        {
            try
            {
                TenantDescriptor? descriptor = await catalog.FindAsync(dossier, cancellationToken).ConfigureAwait(false);
                if (descriptor is null)
                {
                    failed++;
                    continue;
                }

                DateTimeOffset now = clock.GetUtcNow();
                DateOnly today = LegalCalendar.DateOf(now);
                IReadOnlyList<DossierIndicator> indicators = await DossierJobScope.RunAsync(scopes, descriptor, async (provider, token) =>
                {
                    List<DossierIndicator> all = [];
                    foreach (IDossierIndicatorSource source in provider.GetServices<IDossierIndicatorSource>())
                    {
                        try
                        {
                            all.AddRange(await source.ComputeAsync(today, token).ConfigureAwait(false));
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            LogSourceFailed(dossier.Value, source.GetType().Name, exception);
                        }
                    }

                    return (IReadOnlyList<DossierIndicator>)all;
                }, cancellationToken).ConfigureAwait(false);

                await store.ReplaceAsync(dossier, indicators, now, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                LogDossierFailed(dossier.Value, exception);
            }
        }

        return new IndicatorRefreshOutcome(refreshed, failed);
    }


    [LoggerMessage(Level = LogLevel.Warning, Message = "Vue cabinet : indicateur {Source} du dossier {DossierId} en échec")]
    private partial void LogSourceFailed(Guid dossierId, string source, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vue cabinet : calcul du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}

/// <summary>
/// Indicateurs gardés en mémoire : poste isolé et hôtes sans base Catalog. Ce ne sont que des agrégats recalculables ;
/// la vue les recalcule quand ils manquent.
/// </summary>
internal sealed class InMemoryDossierIndicatorStore : IDossierIndicatorStore
{
    private readonly ConcurrentDictionary<DossierId, StoredDossierIndicators> _rows = new();

    public Task ReplaceAsync(DossierId dossier, IReadOnlyList<DossierIndicator> indicators, DateTimeOffset computedAt, CancellationToken cancellationToken)
    {
        _rows[dossier] = new StoredDossierIndicators(dossier, computedAt, [.. indicators]);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredDossierIndicators>> ReadAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        IReadOnlyList<StoredDossierIndicators> found = [.. dossiers.Select(d => _rows.GetValueOrDefault(d)).OfType<StoredDossierIndicators>()];
        return Task.FromResult(found);
    }
}
