using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.Billing;

namespace Lettra.Application.Tests;

/// <summary>Indicateurs de ventes d'un dossier : des ratios, des délais et des indices, jamais un montant publié.</summary>
public sealed class BillingKpiCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly Guid Durand = Guid.CreateVersion7();
    private static readonly Guid Martin = Guid.CreateVersion7();

    private static InvoiceKpiFact Invoice(DateOnly issued, decimal exclTax, Guid? customer = null, decimal paid = 0m, decimal credited = 0m, DateOnly? due = null, InvoiceKind kind = InvoiceKind.Invoice) =>
        new(kind, issued, due ?? issued.AddDays(30), customer ?? Durand, exclTax, exclTax * 1.2m, credited, paid);

    private static double Value(IEnumerable<KpiSample> samples, string metric, string? label = null) =>
        samples.Single(s => s.Metric == metric && (label is null || s.Label?.Value == label)).Value;

    private static IReadOnlyList<KpiSample> Compute(IEnumerable<InvoiceKpiFact> invoices, IEnumerable<QuoteKpiFact>? quotes = null) =>
        BillingKpiCalculator.Compute(new BillingKpiFacts([.. invoices], [.. quotes ?? []]), Today);

    [Fact]
    public void Un_dossier_sans_piece_publie_des_compteurs_a_zero_et_aucun_ratio_sans_base()
    {
        IReadOnlyList<KpiSample> samples = Compute([]);

        samples.Where(s => s.Metric == KpiMetrics.InvoicesIssued).Should().HaveCount(12).And.OnlyContain(s => s.Value == 0);
        samples.Select(s => s.Label?.Value).Where(m => m is not null).Should().Contain(["2025-11", "2026-10"]);
        Value(samples, KpiMetrics.OverdueReceivables).Should().Be(0);
        Value(samples, KpiMetrics.DaysSalesOutstanding).Should().Be(0, "rien n'est vendu ni dû");
        Value(samples, KpiMetrics.ActiveCustomers).Should().Be(0);
        Value(samples, KpiMetrics.QuotesPending).Should().Be(0);
        samples.Select(s => s.Metric).Should().NotContain(
            [KpiMetrics.RevenueIndex, KpiMetrics.RevenueTrend, KpiMetrics.Collection, KpiMetrics.TopCustomerShare, KpiMetrics.QuoteConversion]);
    }

    [Fact]
    public void L_indice_mensuel_vaut_100_pour_le_mois_moyen_et_les_avoirs_viennent_en_deduction()
    {
        IReadOnlyList<KpiSample> samples = Compute(
        [
            Invoice(new DateOnly(2026, 9, 10), 1_000m),
            Invoice(new DateOnly(2026, 10, 2), 2_000m),
            Invoice(new DateOnly(2026, 10, 5), 200m, kind: InvoiceKind.CreditNote),
            Invoice(new DateOnly(2025, 10, 31), 5_000m), // hors des 12 mois
        ]);

        // 2 800 € HT sur 12 mois : 233,33 € par mois en moyenne.
        Value(samples, KpiMetrics.RevenueIndex, "2026-09").Should().Be(428.6);
        Value(samples, KpiMetrics.RevenueIndex, "2026-10").Should().Be(771.4);
        Value(samples, KpiMetrics.RevenueIndex, "2026-01").Should().Be(0);
        Value(samples, KpiMetrics.InvoicesIssued, "2026-10").Should().Be(1, "un avoir n'est pas une facture émise");
        samples.Should().NotContain(s => s.Label != null && s.Label.Value == "2025-10");
    }

    [Fact]
    public void L_evolution_compare_les_trois_derniers_mois_complets_aux_trois_d_avant()
    {
        IReadOnlyList<KpiSample> samples = Compute(
        [
            Invoice(new DateOnly(2026, 4, 15), 1_000m),
            Invoice(new DateOnly(2026, 7, 15), 600m),
            Invoice(new DateOnly(2026, 9, 30), 900m),
            Invoice(new DateOnly(2026, 10, 1), 10_000m), // mois en cours : pas encore complet
        ]);

        Value(samples, KpiMetrics.RevenueTrend).Should().Be(0.5);
    }

    [Fact]
    public void Le_delai_de_paiement_et_la_part_en_retard_se_lisent_sur_le_reste_a_encaisser()
    {
        IReadOnlyList<KpiSample> samples = Compute(
        [
            Invoice(new DateOnly(2026, 9, 1), 1_000m, paid: 1_200m),                          // payée
            Invoice(new DateOnly(2026, 8, 1), 500m, due: new DateOnly(2026, 9, 1)),           // 600 € TTC en retard
            Invoice(new DateOnly(2026, 10, 10), 500m, credited: 120m),                        // 480 € TTC à échoir
            Invoice(new DateOnly(2026, 10, 12), 100m, kind: InvoiceKind.CreditNote),          // avoir : jamais une créance
        ]);

        // Reste à encaisser 1 080 € TTC dont 600 € échus ; ventes TTC des 90 jours : 1 200 + 600 + 600 − 120 = 2 280 €.
        Value(samples, KpiMetrics.OverdueReceivables).Should().Be(0.5556);
        Value(samples, KpiMetrics.DaysSalesOutstanding).Should().Be(42.6);
        // Encaissé 1 200 € sur 1 200 + 600 + (600 − 120) = 2 280 € dus.
        Value(samples, KpiMetrics.Collection).Should().Be(0.5263);
    }

    [Fact]
    public void Une_facture_refusee_par_l_acheteur_n_est_plus_une_creance_a_suivre()
    {
        IReadOnlyList<KpiSample> samples = Compute(
        [
            Invoice(new DateOnly(2026, 8, 1), 500m, due: new DateOnly(2026, 9, 1)) with { Refused = true },
            Invoice(new DateOnly(2026, 8, 1), 500m, due: new DateOnly(2026, 9, 1), paid: 300m),
        ]);

        Value(samples, KpiMetrics.OverdueReceivables).Should().Be(1, "seule la facture acceptée reste due, et elle est échue");
        Value(samples, KpiMetrics.Collection).Should().Be(0.5, "300 € encaissés sur les 600 € de la facture acceptée");
        Value(samples, KpiMetrics.ActiveCustomers).Should().Be(1, "la vente refusée compte toujours dans l'activité");
    }

    [Fact]
    public void Sans_vente_recente_mais_avec_un_reste_du_le_delai_n_est_pas_publie()
    {
        IReadOnlyList<KpiSample> samples = Compute([Invoice(new DateOnly(2026, 1, 5), 100m)]);

        samples.Should().NotContain(s => s.Metric == KpiMetrics.DaysSalesOutstanding);
        Value(samples, KpiMetrics.OverdueReceivables).Should().Be(1);
    }

    [Fact]
    public void La_dependance_au_premier_client_et_les_clients_actifs()
    {
        IReadOnlyList<KpiSample> samples = Compute(
        [
            Invoice(new DateOnly(2026, 3, 1), 300m, Durand),
            Invoice(new DateOnly(2026, 5, 1), 700m, Martin),
            Invoice(new DateOnly(2026, 6, 1), 100m, Martin, kind: InvoiceKind.CreditNote),
        ]);

        Value(samples, KpiMetrics.ActiveCustomers).Should().Be(2);
        Value(samples, KpiMetrics.TopCustomerShare).Should().Be(0.6667, "Martin fait 600 € HT sur 900 €");
    }

    [Fact]
    public void La_transformation_des_devis_compte_les_refus_et_les_expirations()
    {
        IReadOnlyList<KpiSample> samples = Compute([],
        [
            new QuoteKpiFact(QuoteStatus.Accepted, new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)),
            new QuoteKpiFact(QuoteStatus.Invoiced, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1)),
            new QuoteKpiFact(QuoteStatus.Refused, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)),
            new QuoteKpiFact(QuoteStatus.Validated, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)),   // expiré
            new QuoteKpiFact(QuoteStatus.Validated, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 24)), // expire bientôt
            new QuoteKpiFact(QuoteStatus.Validated, new DateOnly(2026, 10, 15), null),                     // sans fin de validité
            new QuoteKpiFact(QuoteStatus.Accepted, new DateOnly(2025, 6, 1), null),                        // hors des 12 mois
        ]);

        Value(samples, KpiMetrics.QuoteConversion).Should().Be(0.5);
        Value(samples, KpiMetrics.QuotesPending).Should().Be(2);
        Value(samples, KpiMetrics.QuotesExpiringSoon).Should().Be(1);
    }

    [Fact]
    public void Seuls_des_indicateurs_du_catalogue_sont_produits()
    {
        IReadOnlyList<KpiSample> samples = Compute([Invoice(new DateOnly(2026, 10, 1), 100m)], [new QuoteKpiFact(QuoteStatus.Refused, Today, null)]);

        samples.Select(s => s.Metric).Should().OnlyContain(m => KpiMetrics.Help.ContainsKey(m) && KpiMetrics.Business.Contains(m));
        samples.Should().OnlyContain(s => s.Dossier == null && (s.Label == null || s.Label.Name == KpiMetrics.Labels.Month));
    }

    [Fact]
    public async Task La_source_lit_les_pieces_puis_calcule()
    {
        BillingKpiSource source = new(new FixedReader(new BillingKpiFacts([Invoice(Today, 100m)], [])));

        IReadOnlyList<KpiSample> samples = await source.CollectAsync(Today, default);

        Value(samples, KpiMetrics.ActiveCustomers).Should().Be(1);
    }

    private sealed class FixedReader(BillingKpiFacts facts) : IBillingKpiReader
    {
        public Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(facts);
    }
}
