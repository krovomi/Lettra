using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Pilotage;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>
/// La comparaison aux pairs : les autres dossiers du cabinet de la même division NAF, lus dans la dernière collecte.
/// Rien n'est rendu sous cinq pairs, et un indicateur que moins de cinq pairs ont n'est pas comparé.
/// </summary>
public sealed class PeerComparisonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());
    private static readonly OrganisationId Cabinet = new(Guid.CreateVersion7());

    [Theory]
    [InlineData(new double[] { 3, 1, 2 }, 2)]
    [InlineData(new double[] { 4, 1, 3, 2 }, 2.5)]
    [InlineData(new double[] { 7 }, 7)]
    public void La_mediane_est_la_valeur_du_milieu(double[] values, double expected) =>
        PeerBenchmark.Median(values).Should().Be(expected);

    [Fact]
    public void Sous_cinq_pairs_du_meme_secteur_rien_n_est_compare()
    {
        KpiSample[] candidates = [.. Peer(62, 30), .. Peer(62, 40), .. Peer(62, 50), .. Peer(62, 60), .. Peer(47, 70), .. Peer(47, 80)];

        PeerComparisonResponse response = PeerBenchmark.Compare("62.02A", [new KpiSample(KpiMetrics.DaysSalesOutstanding, 45)], Now, candidates);

        response.Should().Be(new PeerComparisonResponse(PeerComparisonStatusDto.TooFewPeers, "62.02A", "62", 4, Now, []));
    }

    [Fact]
    public void Chaque_indicateur_se_place_par_rapport_a_la_mediane_dans_le_bon_sens()
    {
        KpiSample[] candidates =
        [
            .. Peer(62, 30, overdue: 0.10, collection: 0.90),
            .. Peer(62, 40, overdue: 0.20, collection: 0.85),
            .. Peer(62, 50, overdue: 0.30, collection: 0.80),
            .. Peer(62, 60, overdue: 0.40, collection: 0.75),
            .. Peer(62, 70, overdue: 0.50),
            .. Peer(47, 10, overdue: 0.0, collection: 1.0),
        ];
        KpiSample[] own =
        [
            new(KpiMetrics.DaysSalesOutstanding, 41),
            new(KpiMetrics.OverdueReceivables, 0.45),
        ];

        PeerComparisonResponse response = PeerBenchmark.Compare("62.02A", own, Now, candidates);

        response.Status.Should().Be(PeerComparisonStatusDto.Available);
        response.Sector.Should().Be("62");
        response.Peers.Should().Be(5, "le dossier de la division 47 n'est pas un pair");
        response.Metrics.Should().Equal(
            new PeerMetricComparisonDto(PeerMetricDto.DaysSalesOutstanding, 41, 50, 5, PeerPositionDto.Better),
            new PeerMetricComparisonDto(PeerMetricDto.OverdueReceivables, 0.45, 0.30, 5, PeerPositionDto.Worse));
        response.Metrics.Select(m => m.Metric).Should().NotContain(PeerMetricDto.Collection, "seuls quatre pairs ont un taux d'encaissement");
    }

    [Theory]
    [InlineData(52.5, PeerPositionDto.Similar)]
    [InlineData(53.5, PeerPositionDto.Worse)]
    [InlineData(46.5, PeerPositionDto.Better)]
    public void Un_ecart_de_trois_jours_au_plus_est_comparable(double own, PeerPositionDto expected)
    {
        KpiSample[] candidates = [.. Peer(62, 30), .. Peer(62, 40), .. Peer(62, 50), .. Peer(62, 60), .. Peer(62, 70)];

        PeerComparisonResponse response = PeerBenchmark.Compare("62.01Z", [new KpiSample(KpiMetrics.DaysSalesOutstanding, own)], Now, candidates);

        response.Metrics.Should().ContainSingle().Which.Position.Should().Be(expected);
    }

    [Fact]
    public void Sans_valeur_propre_l_indicateur_montre_la_mediane_sans_position()
    {
        KpiSample[] candidates = [.. Peer(62, 30), .. Peer(62, 40), .. Peer(62, 50), .. Peer(62, 60), .. Peer(62, 70)];

        PeerComparisonResponse response = PeerBenchmark.Compare("62.01Z", [], Now, candidates);

        response.Metrics.Should().ContainSingle().Which.Should().Be(new PeerMetricComparisonDto(PeerMetricDto.DaysSalesOutstanding, null, 50, 5, null));
    }

    [Fact]
    public async Task Le_secteur_vient_de_la_fiche_entreprise_et_ne_se_publie_qu_une_fois_renseigne()
    {
        (await new CompanyKpiSource(new Profiles(null)).CollectAsync(default, default)).Should().BeEmpty();
        (await new CompanyKpiSource(new Profiles(Profile(null))).CollectAsync(default, default)).Should().BeEmpty();
        (await new CompanyKpiSource(new Profiles(Profile("62.02A"))).CollectAsync(default, default))
            .Should().Equal(new KpiSample(KpiMetrics.DossierSector, 1, Label: new KpiLabel(KpiMetrics.Labels.Sector, "62")));
    }

    [Fact]
    public async Task Le_dossier_se_compare_aux_seuls_autres_dossiers_de_son_cabinet()
    {
        DossierSummary[] cabinet = [new(Dossier.Value, "Atelier"), .. Enumerable.Range(0, 5).Select(i => new DossierSummary(Guid.CreateVersion7(), $"Pair {i}"))];
        KpiSample[] collected =
        [
            .. cabinet.Skip(1).SelectMany((d, i) => Peer(62, 30 + (10 * i), id: new DossierId(d.Id))),
            .. Peer(62, 500, id: Dossier),
            .. Peer(62, 900),
        ];
        PeerComparisonHandler handler = Handler(Profile("62.02A"), new FixedSnapshot(new KpiSnapshot(Now, [], collected)), cabinet);

        PeerComparisonResponse response = (await handler.HandleAsync(new GetPeerComparisonQuery(Dossier), default)).Value;

        response.Status.Should().Be(PeerComparisonStatusDto.Available);
        response.Peers.Should().Be(5, "ni le dossier lui-même ni un dossier d'un autre cabinet ne comptent");
        response.Metrics.Should().ContainSingle().Which.Median.Should().Be(50);
        response.Metrics[0].Value.Should().Be(0, "sa valeur est calculée à l'instant (aucune vente), pas lue dans la collecte (500)");
    }

    [Fact]
    public async Task Sans_code_naf_ou_sans_collecte_la_comparaison_dit_pourquoi()
    {
        (await Handler(Profile(null), null, []).HandleAsync(new GetPeerComparisonQuery(Dossier), default)).Value.Status
            .Should().Be(PeerComparisonStatusDto.NoSector);
        PeerComparisonResponse offline = (await Handler(Profile("43.39Z"), null, []).HandleAsync(new GetPeerComparisonQuery(Dossier), default)).Value;
        offline.Should().Be(new PeerComparisonResponse(PeerComparisonStatusDto.NoCollection, "43.39Z", "43", 0, null, []));
        new GetPeerComparisonQuery(Dossier).Permission.Should().Be(Permission.DossierLire);
    }

    private static PeerComparisonHandler Handler(CompanyProfile profile, IKpiSnapshotProvider? snapshot, IReadOnlyList<DossierSummary> cabinet) => new(
        new Profiles(profile),
        new NoSales(),
        new FakeTenantContext { Descriptor = new TenantDescriptor(Dossier, Cabinet, DatabaseProvider.Sqlite, "config:test") },
        new Directory(cabinet),
        snapshot is null ? [] : [snapshot],
        TimeProvider.System);

    private static KpiSample[] Peer(int division, double dso, double? overdue = null, double? collection = null, DossierId? id = null)
    {
        DossierId dossier = id ?? new DossierId(Guid.CreateVersion7());
        List<KpiSample> samples =
        [
            new(KpiMetrics.DossierSector, 1, dossier, new KpiLabel(KpiMetrics.Labels.Sector, division.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            new(KpiMetrics.DaysSalesOutstanding, dso, dossier),
        ];
        if (overdue is double o)
        {
            samples.Add(new(KpiMetrics.OverdueReceivables, o, dossier));
        }

        if (collection is double c)
        {
            samples.Add(new(KpiMetrics.Collection, c, dossier));
        }

        return [.. samples];
    }

    private static CompanyProfile Profile(string? activity) => CompanyProfile.Create(
        new CompanyProfile.Draft(
            "Atelier Numérique", "SARL", null, "552100554", null, null, "FR96552100554", "1 rue des Exemples", null, "69002", "Lyon", "FR",
            VatFranchise: false, VatOnDebits: false, PaymentDays: 30, null, null, null, null, null, ActivityCode: activity),
        Now).Value;

    private sealed class Profiles(CompanyProfile? profile) : ICompanyProfileRepository
    {
        public Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(profile);

        public Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoSales : IBillingKpiReader
    {
        public Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new BillingKpiFacts([], []));
    }

    private sealed class FixedSnapshot(KpiSnapshot snapshot) : IKpiSnapshotProvider
    {
        public Task<KpiSnapshot> GetAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class Directory(IReadOnlyList<DossierSummary> dossiers) : IDossierDirectory
    {
        public Task<IReadOnlyList<DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
            Task.FromResult(dossiers);
    }
}
