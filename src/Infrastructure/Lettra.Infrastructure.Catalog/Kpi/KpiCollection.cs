using System.Diagnostics;
using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Catalog;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Catalog.Kpi;

/// <summary>Indicateurs de pilotage lus par le Prometheus de la cellule (docs/indicateurs-grafana.md).</summary>
public sealed class KpiOptions
{
    public const string SectionName = "Lettra:Kpi";

    /// <summary>
    /// Jeton que Prometheus présente (<c>Authorization: Bearer …</c>). Sans jeton, le point d'accès n'existe pas : rien
    /// n'est exposé par défaut.
    /// </summary>
    public string? ScrapeToken { get; set; }

    /// <summary>Âge au-delà duquel une lecture refait la collecte (chaque base de dossier est rouverte).</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Publie aussi vers Prometheus les indicateurs de gestion des dossiers (ventes, devis, vue cabinet). Faux pour une
    /// cellule SaaS dont le Grafana ne sert qu'à l'exploitant : il n'en reçoit alors que l'exploitation. Les tableaux de
    /// bord dans Lettra, qui appliquent les droits de chacun, les montrent toujours.
    /// </summary>
    public bool BusinessIndicators { get; set; } = true;
}

/// <summary>
/// Collecte les indicateurs : ouvre chaque dossier actif dans sa portée (comme les passes du Worker), y fait tourner les
/// <see cref="IDossierKpiSource"/>, puis les <see cref="ICellKpiSource"/>. Une source ou un dossier en échec n'arrête
/// pas les autres ; il est compté. Le résultat est gardé et resservi tant qu'il a moins de
/// <see cref="KpiOptions.RefreshInterval"/>. Aucune donnée de dossier n'est journalisée.
/// </summary>
internal sealed partial class KpiSnapshotCollector(
    IServiceScopeFactory scopes,
    IDbContextFactory<CatalogDbContext> catalog,
    ITenantCatalog tenants,
    IEnumerable<ICellKpiSource> cellSources,
    IOptions<KpiOptions> options,
    TimeProvider clock,
    ILogger<KpiSnapshotCollector> logger) : IKpiSnapshotProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KpiSnapshot? _last;

    public async Task<KpiSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (Fresh(_last) is { } fresh)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Une autre lecture a peut-être collecté pendant l'attente.
            if (Fresh(_last) is { } done)
            {
                return done;
            }

            _last = await CollectAsync(cancellationToken).ConfigureAwait(false);
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private KpiSnapshot? Fresh(KpiSnapshot? snapshot) =>
        snapshot is not null && clock.GetUtcNow() - snapshot.CollectedAt < options.Value.RefreshInterval ? snapshot : null;

    internal async Task<KpiSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.DateOf(now);
        IReadOnlyList<KpiDossier> dossiers = await ActiveDossiersAsync(cancellationToken).ConfigureAwait(false);
        List<KpiSample> samples = [];
        int failures = 0;

        foreach (KpiDossier dossier in dossiers)
        {
            try
            {
                TenantDescriptor? descriptor = await tenants.FindAsync(dossier.Id, cancellationToken).ConfigureAwait(false);
                if (descriptor is null)
                {
                    failures++;
                    continue;
                }

                (List<KpiSample> collected, int failed) = await DossierJobScope.RunAsync(scopes, descriptor, async (provider, token) =>
                {
                    List<KpiSample> found = [];
                    int sourceFailures = 0;
                    foreach (IDossierKpiSource source in provider.GetServices<IDossierKpiSource>())
                    {
                        try
                        {
                            found.AddRange(await source.CollectAsync(today, token).ConfigureAwait(false));
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            sourceFailures++;
                            LogSourceFailed(dossier.Id.Value, source.GetType().Name, exception);
                        }
                    }

                    return (found, sourceFailures);
                }, cancellationToken).ConfigureAwait(false);

                failures += failed;
                samples.Add(new KpiSample(KpiMetrics.DossierInfo, 1, dossier.Id));
                samples.AddRange(collected.Select(s => s with { Dossier = dossier.Id }));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                LogDossierFailed(dossier.Id.Value, exception);
            }
        }

        DossierId[] ids = [.. dossiers.Select(d => d.Id)];
        foreach (ICellKpiSource source in cellSources)
        {
            try
            {
                samples.AddRange(await source.CollectAsync(ids, today, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                LogCellSourceFailed(source.GetType().Name, exception);
            }
        }

        samples.Add(new KpiSample(KpiMetrics.CollectionDuration, Stopwatch.GetElapsedTime(started).TotalSeconds));
        samples.Add(new KpiSample(KpiMetrics.CollectionFailures, failures));
        samples.Add(new KpiSample(KpiMetrics.CollectedAt, now.ToUnixTimeMilliseconds() / 1000d));
        LogCollected(dossiers.Count, failures);
        return new KpiSnapshot(now, dossiers, samples);
    }

    private async Task<IReadOnlyList<KpiDossier>> ActiveDossiersAsync(CancellationToken cancellationToken)
    {
        CatalogDbContext db = await catalog.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = await db.Dossiers.AsNoTracking()
                .Where(d => d.State == DossierState.Active)
                .Join(db.Organisations.AsNoTracking(), d => d.OrganisationId, o => o.Id, (d, o) => new { d.Id, d.Name, Cabinet = o.Name })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return [.. rows.OrderBy(r => r.Name, StringComparer.CurrentCulture).Select(r => new KpiDossier(new DossierId(r.Id), r.Name, r.Cabinet))];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Indicateurs : {Dossiers} dossiers collectés, {Failures} échecs")]
    private partial void LogCollected(int dossiers, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indicateurs : source {Source} du dossier {DossierId} en échec")]
    private partial void LogSourceFailed(Guid dossierId, string source, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Indicateurs : collecte du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Indicateurs : source {Source} de la cellule en échec")]
    private partial void LogCellSourceFailed(string source, Exception exception);
}

/// <summary>
/// Ce que la base Catalog sait déjà, sans ouvrir de dossier : comptes par état, dossiers par état, et les compteurs de la
/// vue cabinet calculés la nuit (avec leur fraîcheur, qui dit si la passe de nuit tourne).
/// </summary>
internal sealed class CatalogKpiSource(IDbContextFactory<CatalogDbContext> catalog, IDossierIndicatorStore indicators, TimeProvider clock) : ICellKpiSource
{
    public async Task<IReadOnlyList<KpiSample>> CollectAsync(IReadOnlyCollection<DossierId> dossiers, DateOnly today, CancellationToken cancellationToken)
    {
        List<KpiSample> samples = [];
        CatalogDbContext db = await catalog.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var accounts = await db.UserAccounts.AsNoTracking()
                .GroupBy(a => a.State).Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            // Les comptes supprimés ne sont plus des personnes du cabinet : ils ne sont pas comptés.
            samples.AddRange(Enum.GetValues<AccountState>().Where(s => s != AccountState.Deleted).Select(state => new KpiSample(
                KpiMetrics.Accounts,
                accounts.Where(a => a.State == state).Sum(a => a.Count),
                Label: new KpiLabel(KpiMetrics.Labels.State, state.ToString().ToLowerInvariant()))));

            var cells = await db.Dossiers.AsNoTracking()
                .GroupBy(d => d.State).Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            samples.AddRange(Enum.GetValues<DossierState>().Select(state => new KpiSample(
                KpiMetrics.Dossiers,
                cells.Where(d => d.State == state).Sum(d => d.Count),
                Label: new KpiLabel(KpiMetrics.Labels.State, state.ToString().ToLowerInvariant()))));
        }

        DateTimeOffset now = clock.GetUtcNow();
        foreach (StoredDossierIndicators stored in await indicators.ReadAsync(dossiers, cancellationToken).ConfigureAwait(false))
        {
            samples.Add(new KpiSample(KpiMetrics.CabinetIndicatorAge, Math.Max(0, (now - stored.ComputedAt).TotalSeconds), stored.Dossier));
            foreach (DossierIndicator indicator in stored.Indicators)
            {
                KpiLabel key = new(KpiMetrics.Labels.Key, indicator.Key);
                samples.Add(new KpiSample(KpiMetrics.CabinetIndicatorCount, indicator.Count, stored.Dossier, key));
                samples.Add(new KpiSample(KpiMetrics.CabinetIndicatorLevel, (int)indicator.Level, stored.Dossier, key));
                if (indicator.DueOn is DateOnly due)
                {
                    samples.Add(new KpiSample(KpiMetrics.CabinetIndicatorDueDays, due.DayNumber - today.DayNumber, stored.Dossier, key));
                }
            }
        }

        return samples;
    }
}

public static class KpiCollectionRegistration
{
    /// <summary>Collecte des indicateurs de pilotage (modes 2, 3 et 5) ; réglages sous <c>Lettra:Kpi</c>.</summary>
    public static IServiceCollection AddKpiCollection(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<KpiOptions>().Bind(configuration.GetSection(KpiOptions.SectionName));
        services.AddSingleton<ICellKpiSource, CatalogKpiSource>();
        services.AddSingleton<KpiSnapshotCollector>();
        services.AddSingleton<IKpiSnapshotProvider>(sp => sp.GetRequiredService<KpiSnapshotCollector>());
        return services;
    }
}
