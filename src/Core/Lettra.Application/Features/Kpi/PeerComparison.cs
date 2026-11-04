using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pilotage;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Kpi;

/// <summary>
/// Le dossier ouvert comparé aux autres dossiers du cabinet du même secteur (division NAF). Même droit que son tableau de
/// bord : seules des médianes d'au moins cinq dossiers sont rendues, jamais un dossier ni un nom.
/// </summary>
public sealed record GetPeerComparisonQuery(DossierId Dossier) : IQuery<Result<PeerComparisonResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Publie le secteur du dossier pour la collecte (<see cref="KpiMetrics.DossierSector"/>) : c'est ainsi que les pairs
/// sont trouvés sans rouvrir leur base. Rien n'est publié tant que la fiche entreprise n'a pas de code NAF.
/// </summary>
public sealed class CompanyKpiSource(ICompanyProfileRepository profiles) : IDossierKpiSource
{
    public async Task<IReadOnlyList<KpiSample>> CollectAsync(DateOnly today, CancellationToken cancellationToken)
    {
        string? code = (await profiles.FindAsync(cancellationToken).ConfigureAwait(false))?.ActivityCode;
        return code is null ? [] : [new KpiSample(KpiMetrics.DossierSector, 1, Label: new KpiLabel(KpiMetrics.Labels.Sector, NafCode.Division(code)))];
    }
}

/// <summary>
/// La comparaison du dossier ouvert à ses pairs, partagée par l'écran des tableaux de bord et le radar de prévention. Le
/// secteur vient de la fiche entreprise du dossier, ses valeurs sont calculées à l'instant (comme son tableau de bord) ;
/// les pairs sont lus dans la dernière collecte, parmi les dossiers du même cabinet.
/// </summary>
public sealed class DossierPeerComparison(
    ICompanyProfileRepository profiles,
    IBillingKpiReader billing,
    ITenantContext tenant,
    IDossierDirectory directory,
    IEnumerable<IKpiSnapshotProvider> snapshots,
    TimeProvider clock)
{
    public async Task<PeerComparisonResponse> CompareAsync(DossierId dossier, CancellationToken cancellationToken)
    {
        string? code = (await profiles.FindAsync(cancellationToken).ConfigureAwait(false))?.ActivityCode;
        if (code is null)
        {
            return PeerBenchmark.Unavailable(PeerComparisonStatusDto.NoSector, null, null);
        }

        if (snapshots.FirstOrDefault() is not { } provider)
        {
            return PeerBenchmark.Unavailable(PeerComparisonStatusDto.NoCollection, code, null);
        }

        KpiSnapshot snapshot = await provider.GetAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DossierSummary> cabinet = await directory.ListAsync(tenant.Current.OrganisationId, cancellationToken).ConfigureAwait(false);
        BillingKpiFacts facts = await billing.ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KpiSample> own = BillingKpiCalculator.Compute(facts, LegalCalendar.DateOf(clock.GetUtcNow()));
        HashSet<Guid> others = [.. cabinet.Select(d => d.Id).Where(id => id != dossier.Value)];
        return PeerBenchmark.Compare(code, own, snapshot.CollectedAt, snapshot.Samples.Where(s => s.Dossier is { } id && others.Contains(id.Value)));
    }
}

internal sealed class PeerComparisonHandler(DossierPeerComparison comparison) : IQueryHandler<GetPeerComparisonQuery, Result<PeerComparisonResponse>>
{
    public async Task<Result<PeerComparisonResponse>> HandleAsync(GetPeerComparisonQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await comparison.CompareAsync(query.Dossier, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Le calcul de la comparaison, sans base : quels dossiers sont des pairs, la médiane de chaque indicateur, et la
/// position du dossier. Un indicateur que moins de cinq pairs ont n'est pas comparé.
/// </summary>
public static class PeerBenchmark
{
    /// <summary>Un délai de paiement à trois jours près de la médiane est dit comparable.</summary>
    public const double SimilarDays = 3;

    /// <summary>Un ratio à trois points près de la médiane est dit comparable.</summary>
    public const double SimilarRatio = 0.03;

    /// <summary>Les indicateurs comparés, leur nom dans la collecte, et si une valeur plus basse est meilleure.</summary>
    public static IReadOnlyList<(PeerMetricDto Metric, string Kpi, bool LowerIsBetter)> Metrics { get; } =
    [
        (PeerMetricDto.DaysSalesOutstanding, KpiMetrics.DaysSalesOutstanding, true),
        (PeerMetricDto.OverdueReceivables, KpiMetrics.OverdueReceivables, true),
        (PeerMetricDto.Collection, KpiMetrics.Collection, false),
        (PeerMetricDto.RevenueTrend, KpiMetrics.RevenueTrend, false),
        (PeerMetricDto.QuoteConversion, KpiMetrics.QuoteConversion, false),
        (PeerMetricDto.TopCustomerShare, KpiMetrics.TopCustomerShare, true),
        (PeerMetricDto.CreditNotes, KpiMetrics.CreditNotes, true),
    ];

    public static PeerComparisonResponse Unavailable(PeerComparisonStatusDto status, string? code, DateTimeOffset? collectedAt, int peers = 0) =>
        new(status, code, code is null ? null : NafCode.Division(code), peers, collectedAt, []);

    /// <summary>
    /// <paramref name="own"/> : les valeurs du dossier. <paramref name="candidates"/> : les valeurs collectées des autres
    /// dossiers du cabinet ; sont des pairs ceux dont le secteur collecté est celui de <paramref name="code"/>.
    /// </summary>
    public static PeerComparisonResponse Compare(string code, IEnumerable<KpiSample> own, DateTimeOffset collectedAt, IEnumerable<KpiSample> candidates)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(candidates);
        string sector = NafCode.Division(code);
        ILookup<DossierId, KpiSample> byDossier = candidates.Where(s => s.Dossier is not null).ToLookup(s => s.Dossier!.Value);
        IGrouping<DossierId, KpiSample>[] peers =
        [
            .. byDossier.Where(d => d.Any(s => s.Metric == KpiMetrics.DossierSector && s.Label is { Name: KpiMetrics.Labels.Sector } label && label.Value == sector)),
        ];
        if (peers.Length < PeerComparisonResponse.MinimumPeers)
        {
            return Unavailable(PeerComparisonStatusDto.TooFewPeers, code, collectedAt, peers.Length);
        }

        KpiSample[] mine = [.. own];
        List<PeerMetricComparisonDto> metrics = [];
        foreach ((PeerMetricDto metric, string kpi, bool lowerIsBetter) in Metrics)
        {
            double[] values = [.. peers.Select(p => Value(p, kpi)).OfType<double>()];
            if (values.Length < PeerComparisonResponse.MinimumPeers)
            {
                continue;
            }

            double median = Median(values);
            double? value = Value(mine, kpi);
            metrics.Add(new PeerMetricComparisonDto(metric, value, median, values.Length, value is double v ? Position(metric, v, median, lowerIsBetter) : null));
        }

        return new PeerComparisonResponse(PeerComparisonStatusDto.Available, code, sector, peers.Length, collectedAt, metrics);
    }

    /// <summary>La valeur du milieu ; la moyenne des deux du milieu pour un nombre pair de valeurs.</summary>
    public static double Median(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = [.. values.Order()];
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static PeerPositionDto Position(PeerMetricDto metric, double value, double median, bool lowerIsBetter)
    {
        double gap = value - median;
        double similar = metric == PeerMetricDto.DaysSalesOutstanding ? SimilarDays : SimilarRatio;
        if (Math.Abs(gap) <= similar)
        {
            return PeerPositionDto.Similar;
        }

        return gap < 0 == lowerIsBetter ? PeerPositionDto.Better : PeerPositionDto.Worse;
    }

    private static double? Value(IEnumerable<KpiSample> samples, string metric) =>
        samples.FirstOrDefault(s => s.Metric == metric && s.Label is null) is { } sample && double.IsFinite(sample.Value) ? sample.Value : null;
}
