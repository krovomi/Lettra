using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Tableau des délais de paiement (art. D441-6) côté factures émises, et échéances au-delà du plafond légal.</summary>
public sealed class PaymentDelaysReportTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly Closing = new(2026, 12, 31);

    private static PaymentDelayDocument Invoice(
        string number,
        DateOnly issue,
        DateOnly? due,
        decimal total,
        bool business = true,
        bool disputed = false,
        InvoiceKind kind = InvoiceKind.Invoice,
        Guid? credited = null) =>
        new(Id(number), kind, number, "Client " + number, business, issue, due, total, credited, disputed);

    private static Guid Id(string number) => new($"0199a000-0000-7000-8000-{number[^6..].PadLeft(12, '0')}");

    private static PaymentDelaysResponse Compute(IEnumerable<PaymentDelayDocument> documents, params PaymentDelaySettlement[] settlements) =>
        PaymentDelaysReport.Compute(documents, settlements, Start, Closing);

    [Fact]
    public void Chaque_facture_due_a_la_cloture_tombe_dans_sa_tranche_de_retard()
    {
        PaymentDelaysResponse report = Compute(
        [
            Invoice("FAC-000001", new(2026, 11, 1), Closing, 100m),
            Invoice("FAC-000002", new(2026, 11, 1), new(2026, 12, 21), 200m),
            Invoice("FAC-000003", new(2026, 10, 1), new(2026, 11, 15), 300m),
            Invoice("FAC-000004", new(2026, 9, 1), new(2026, 10, 15), 400m),
            Invoice("FAC-000005", new(2026, 6, 1), new(2026, 7, 1), 500m),
            Invoice("FAC-000006", new(2026, 12, 15), new(2027, 1, 15), 600m),
        ]);

        report.Brackets.Select(b => (b.Bracket, b.Count, b.AmountInclTax)).Should().Equal(
            (0, 1, 100m), (1, 1, 200m), (2, 1, 300m), (3, 1, 400m), (4, 1, 500m));
        report.Late.Count.Should().Be(4);
        report.Late.AmountInclTax.Should().Be(1400m);
        report.TurnoverInclTax.Should().Be(2100m);
        report.Late.TurnoverPercent.Should().Be(66.67m);
        report.NotDueCount.Should().Be(1);
        report.NotDueAmountInclTax.Should().Be(600m);
        report.Invoices.Select(i => i.Number).Should().Equal("FAC-000005", "FAC-000004", "FAC-000003", "FAC-000002", "FAC-000001", "FAC-000006");
        report.Invoices[^1].Bracket.Should().BeNull();
        report.Invoices[^1].DaysLate.Should().Be(-15);
    }

    [Fact]
    public void Le_reste_du_se_lit_a_la_cloture_avec_les_avoirs_et_encaissements_de_cette_date()
    {
        PaymentDelayDocument invoice = Invoice("FAC-000001", new(2026, 10, 1), new(2026, 10, 31), 1000m);
        PaymentDelaysResponse report = Compute(
            [
                invoice,
                Invoice("AV-000001", new(2026, 11, 2), null, 100m, kind: InvoiceKind.CreditNote, credited: invoice.InvoiceId),
                Invoice("FAC-000002", new(2026, 10, 1), new(2026, 10, 31), 500m),
            ],
            new PaymentDelaySettlement(invoice.InvoiceId, new(2026, 11, 10), 300m, null),
            new PaymentDelaySettlement(invoice.InvoiceId, new(2026, 11, 20), 200m, new DateOnly(2026, 12, 5)),
            new PaymentDelaySettlement(invoice.InvoiceId, new(2027, 1, 5), 600m, null),
            new PaymentDelaySettlement(Id("FAC-000002"), new(2026, 12, 1), 500m, new DateOnly(2027, 1, 10)));

        report.Invoices.Should().ContainSingle();
        report.Invoices[0].Number.Should().Be("FAC-000001");
        report.Invoices[0].OutstandingInclTax.Should().Be(600m, "1 000 − avoir 100 − 300 encaissés ; la contre-passation de décembre rend les 200 dus");
        report.Invoices[0].Bracket.Should().Be(3);
        report.TurnoverInclTax.Should().Be(1400m);
    }

    [Fact]
    public void Un_encaissement_declare_a_la_plateforme_compte_sans_doubler_celui_saisi()
    {
        PaymentDelayDocument both = Invoice("FAC-000001", new(2026, 10, 1), new(2026, 10, 31), 1000m);
        PaymentDelayDocument declaredOnly = Invoice("FAC-000002", new(2026, 10, 1), new(2026, 10, 31), 500m);
        PaymentDelaysResponse report = Compute(
            [both, declaredOnly],
            new PaymentDelaySettlement(both.InvoiceId, new(2026, 11, 10), 400m, null),
            new PaymentDelaySettlement(both.InvoiceId, new(2026, 11, 10), 400m, null, Declared: true),
            new PaymentDelaySettlement(declaredOnly.InvoiceId, new(2026, 11, 3), 500m, null, Declared: true),
            new PaymentDelaySettlement(declaredOnly.InvoiceId, new(2027, 1, 3), 1m, null, Declared: true));

        report.Invoices.Should().ContainSingle().Which.OutstandingInclTax.Should().Be(600m);
    }

    [Fact]
    public void Une_facture_contestee_est_exclue_des_tranches_et_comptee_en_partie_b()
    {
        PaymentDelaysResponse report = Compute(
        [
            Invoice("FAC-000001", new(2026, 9, 1), new(2026, 10, 1), 800m, disputed: true),
            Invoice("FAC-000002", new(2026, 9, 1), new(2026, 10, 1), 200m),
        ]);

        report.Late.Count.Should().Be(1);
        report.ExcludedCount.Should().Be(1);
        report.ExcludedAmountInclTax.Should().Be(800m);
        report.Invoices.Single(i => i.Disputed).Bracket.Should().BeNull();
    }

    [Fact]
    public void Les_pieces_apres_la_cloture_et_les_factures_reglees_n_entrent_pas()
    {
        PaymentDelayDocument paid = Invoice("FAC-000001", new(2026, 9, 1), new(2026, 10, 1), 200m);
        PaymentDelaysResponse report = Compute(
            [paid, Invoice("FAC-000002", new(2027, 1, 2), new(2027, 2, 1), 900m)],
            new PaymentDelaySettlement(paid.InvoiceId, new(2026, 9, 20), 200m, null));

        report.Invoices.Should().BeEmpty();
        report.TurnoverInclTax.Should().Be(200m);
        report.Late.TurnoverPercent.Should().Be(0m);
    }

    [Fact]
    public void Sans_chiffre_d_affaires_il_n_y_a_pas_de_pourcentage_ni_de_delai_de_reference()
    {
        PaymentDelaysResponse report = Compute([]);

        report.Brackets.Should().HaveCount(5).And.OnlyContain(b => b.Count == 0 && b.TurnoverPercent == null);
        report.ContractualDays.Should().BeNull();
        report.TermBreaches.Should().BeEmpty();
    }

    [Fact]
    public void Le_delai_de_reference_est_le_delai_le_plus_frequent_de_l_exercice()
    {
        PaymentDelaysResponse report = Compute(
        [
            Invoice("FAC-000001", new(2026, 3, 1), new(2026, 3, 31), 100m),
            Invoice("FAC-000002", new(2026, 4, 1), new(2026, 5, 1), 100m),
            Invoice("FAC-000003", new(2026, 5, 1), new(2026, 6, 15), 100m),
            Invoice("FAC-000004", new(2025, 5, 1), new(2025, 6, 15), 100m),
            Invoice("FAC-000005", new(2025, 6, 1), new(2025, 7, 16), 100m),
        ]);

        report.ContractualDays.Should().Be(30);
    }

    [Fact]
    public void Une_echeance_au_dela_du_plafond_legal_est_signalee_pour_un_professionnel_seulement()
    {
        PaymentDelaysResponse report = Compute(
        [
            Invoice("FAC-000001", new(2026, 1, 15), new(2026, 4, 30), 100m),
            Invoice("FAC-000002", new(2026, 1, 15), new(2026, 3, 31), 100m),
            Invoice("FAC-000003", new(2026, 1, 15), new(2026, 6, 30), 100m, business: false),
            Invoice("FAC-000004", new(2026, 2, 1), new(2026, 7, 31), 100m),
            Invoice("FAC-000005", new(2025, 1, 15), new(2025, 6, 30), 100m),
        ]);

        report.TermBreaches.Select(b => (b.Number, b.DaysOver)).Should().Equal(("FAC-000004", 108), ("FAC-000001", 30));
        report.TermBreaches[1].LegalCeiling.Should().Be(new DateOnly(2026, 3, 31));
    }
}
