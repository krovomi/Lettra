using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Cabinet;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pilotage;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Kpi;

/// <summary>
/// Le tableau de bord du dossier ouvert, calculé à l'instant dans sa base. Même droit que la vue du dossier : qui peut
/// le lire voit ses indicateurs.
/// </summary>
public sealed record GetDossierPilotageQuery(DossierId Dossier) : IQuery<Result<DossierPilotageResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Les indicateurs des dossiers que l'utilisateur peut ouvrir (droit de lecture, dossier par dossier), lus dans la
/// dernière collecte du serveur : aucune base de dossier n'est rouverte pour la page.
/// </summary>
public sealed record GetCabinetPilotageQuery : IQuery<Result<CabinetPilotageResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// L'exploitation des dossiers que l'utilisateur administre (droit d'administrer le dossier, dossier par dossier) :
/// place occupée, postes, fraîcheur du calcul quotidien de la vue cabinet.
/// </summary>
public sealed record GetAdministrationPilotageQuery : IQuery<Result<AdministrationPilotageResponse>>, IFilterByPermission
{
    public Permission Permission => Permission.DossierAdministrer;
}

/// <summary>Dans le dossier ouvert : ses pièces lues et calculées à l'instant, et ce qui reste à faire.</summary>
internal sealed class DossierPilotageHandler(
    IBillingKpiReader billing,
    IDossierIndicatorStore indicators,
    ITenantContext tenant,
    IDossierDirectory directory,
    TimeProvider clock) : IQueryHandler<GetDossierPilotageQuery, Result<DossierPilotageResponse>>
{
    public async Task<Result<DossierPilotageResponse>> HandleAsync(GetDossierPilotageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTimeOffset now = clock.GetUtcNow();
        BillingKpiFacts facts = await billing.ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KpiSample> samples = BillingKpiCalculator.Compute(facts, LegalCalendar.DateOf(now));

        IReadOnlyList<DossierSummary> cabinet = await directory.ListAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        string name = cabinet.FirstOrDefault(d => d.Id == query.Dossier.Value)?.Name ?? string.Empty;

        IReadOnlyList<StoredDossierIndicators> read = await indicators.ReadAsync([query.Dossier], cancellationToken).ConfigureAwait(false);
        StoredDossierIndicators? stored = read.Count > 0 ? read[0] : null;
        DossierOverviewRow toDo = CabinetOverviewMapper.ToRow(new DossierSummary(query.Dossier.Value, name), stored);
        return new DossierPilotageResponse(now, PilotageMapper.ToDossier(query.Dossier.Value, name, samples), toDo.ComputedAt, toDo.Indicators);
    }
}

/// <summary>
/// Hors de tout dossier : la dernière collecte du serveur, filtrée dossier par dossier par les droits de l'utilisateur.
/// La collecte couvre toute la cellule ; seule la liste des dossiers accessibles décide de ce qui est rendu.
/// </summary>
internal sealed class CabinetPilotageHandlers(AccessibleDossiers accessible, IEnumerable<IKpiSnapshotProvider> snapshots) :
    IQueryHandler<GetCabinetPilotageQuery, Result<CabinetPilotageResponse>>,
    IQueryHandler<GetAdministrationPilotageQuery, Result<AdministrationPilotageResponse>>
{
    public async Task<Result<CabinetPilotageResponse>> HandleAsync(GetCabinetPilotageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await SnapshotAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return new CabinetPilotageResponse(false, null, []);
        }

        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(query.Permission, cancellationToken).ConfigureAwait(false);
        ILookup<Guid, KpiSample> byDossier = BySnapshotDossier(snapshot);
        DossierPilotageDto[] rows =
        [
            .. dossiers
                .Where(d => byDossier.Contains(d.Id))
                .Select(d => PilotageMapper.ToDossier(d.Id, d.Name, byDossier[d.Id]))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        return new CabinetPilotageResponse(true, snapshot.CollectedAt, rows);
    }

    public async Task<Result<AdministrationPilotageResponse>> HandleAsync(GetAdministrationPilotageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await SnapshotAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return new AdministrationPilotageResponse(false, null, []);
        }

        IReadOnlyList<DossierSummary> dossiers = await accessible.ListAsync(query.Permission, cancellationToken).ConfigureAwait(false);
        ILookup<Guid, KpiSample> byDossier = BySnapshotDossier(snapshot);
        DossierOperationsDto[] rows =
        [
            .. dossiers
                .Where(d => byDossier.Contains(d.Id))
                .Select(d => PilotageMapper.ToOperations(d.Id, d.Name, byDossier[d.Id]))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
        return new AdministrationPilotageResponse(true, snapshot.CollectedAt, rows);
    }

    /// <summary>La dernière collecte du serveur ; aucune là où rien ne collecte (poste isolé, poste relié).</summary>
    private async Task<KpiSnapshot?> SnapshotAsync(CancellationToken cancellationToken) =>
        snapshots.FirstOrDefault() is { } provider ? await provider.GetAsync(cancellationToken).ConfigureAwait(false) : null;

    /// <summary>Les valeurs de chaque dossier collecté (la collecte couvre la cellule ; la liste des droits filtre ensuite).</summary>
    private static ILookup<Guid, KpiSample> BySnapshotDossier(KpiSnapshot snapshot) =>
        snapshot.Samples.Where(s => s.Dossier is not null).ToLookup(s => s.Dossier!.Value.Value);
}

/// <summary>Des valeurs de collecte aux lignes des écrans : une seule traduction pour la page et pour les tests.</summary>
internal static class PilotageMapper
{
    public static DossierPilotageDto ToDossier(Guid id, string name, IEnumerable<KpiSample> samples)
    {
        KpiSample[] all = [.. samples];
        Dictionary<string, MonthlyAccumulator> months = new(StringComparer.Ordinal);
        foreach (KpiSample sample in all.Where(s => s.Label is { Name: KpiMetrics.Labels.Month }))
        {
            MonthlyAccumulator month = months.TryGetValue(sample.Label!.Value, out MonthlyAccumulator? found) ? found : months[sample.Label.Value] = new();
            if (sample.Metric == KpiMetrics.RevenueIndex)
            {
                month.RevenueIndex = sample.Value;
            }
            else if (sample.Metric == KpiMetrics.InvoicesIssued)
            {
                month.InvoicesIssued = (int)sample.Value;
            }
        }

        return new DossierPilotageDto(
            id,
            name,
            Value(all, KpiMetrics.DaysSalesOutstanding),
            Value(all, KpiMetrics.OverdueReceivables),
            Value(all, KpiMetrics.Collection),
            Value(all, KpiMetrics.RevenueTrend),
            Count(all, KpiMetrics.ActiveCustomers),
            Value(all, KpiMetrics.TopCustomerShare),
            Value(all, KpiMetrics.QuoteConversion),
            Count(all, KpiMetrics.QuotesPending),
            Count(all, KpiMetrics.QuotesExpiringSoon),
            [.. months.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => new MonthlyActivityDto(m.Key, m.Value.RevenueIndex, m.Value.InvoicesIssued))]);
    }

    public static DossierOperationsDto ToOperations(Guid id, string name, IEnumerable<KpiSample> samples)
    {
        KpiSample[] all = [.. samples];
        return new DossierOperationsDto(
            id,
            name,
            Value(all, KpiMetrics.DatabaseSize) is double size ? (long)size : null,
            Count(all, KpiMetrics.Devices, "active"),
            Count(all, KpiMetrics.Devices, "revoked"),
            Count(all, KpiMetrics.SilentDevices),
            Value(all, KpiMetrics.OldestDeviceSyncAge),
            Value(all, KpiMetrics.CabinetIndicatorAge));
    }

    private static double? Value(KpiSample[] samples, string metric, string? label = null) =>
        samples.FirstOrDefault(s => s.Metric == metric && (label is null || s.Label?.Value == label)) is { } sample && double.IsFinite(sample.Value)
            ? sample.Value
            : null;

    private static int Count(KpiSample[] samples, string metric, string? label = null) =>
        Value(samples, metric, label) is double value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : 0;

    private sealed class MonthlyAccumulator
    {
        public double? RevenueIndex { get; set; }

        public int InvoicesIssued { get; set; }
    }
}
