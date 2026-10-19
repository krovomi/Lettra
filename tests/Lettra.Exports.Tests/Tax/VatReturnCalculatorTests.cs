using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;
using static Lettra.Exports.Tests.Tax.TaxSamples;

namespace Lettra.Exports.Tests.Tax;

/// <summary>Brouillon de CA3 et de CA12, partie ventes : cases, pièces derrière chaque case, contrôles.</summary>
public sealed class VatReturnCalculatorTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    private static VatReturnContext Context(decimal journalVat, VatReturnFormDto form = VatReturnFormDto.Ca3, IReadOnlyList<string>? drafts = null, IReadOnlyList<string>? notTransmitted = null) =>
        new(form, VatDeclarationRegimeDto.RealNormal, From, To, new DateOnly(2026, 10, 15), journalVat, drafts ?? [], notTransmitted ?? [], false, [], null);

    private static VatReturnBoxResponse Box(VatReturnResponse vat, string key) => vat.Boxes.Single(b => b.Key == key);

    [Fact]
    public void Chaque_operation_va_dans_la_case_de_sa_categorie_et_de_son_taux()
    {
        TaxDocument mixed = Invoice("FAC-000001", new(2026, 9, 3), [Services(1000m), Goods(200m, 5.5m), Services(300m, 10m)]);
        TaxDocument export = Invoice("FAC-000002", new(2026, 9, 10), [Goods(800m, 0m, VatCategoryCodes.Export, "VATEX-EU-G")], regime: VatRegime.Export);
        TaxDocument intraGoods = Invoice("FAC-000003", new(2026, 9, 12), [Goods(500m, 0m, VatCategoryCodes.IntraCommunity, "VATEX-EU-IC")], regime: VatRegime.IntraEuGoodsSupply);
        TaxDocument construction = Invoice("FAC-000004", new(2026, 9, 15), [Services(700m, 0m, VatCategoryCodes.ReverseCharge, "VATEX-FR-AE")], regime: VatRegime.ReverseChargeConstruction);
        TaxDocument disbursement = Invoice("FAC-000005", new(2026, 9, 20), [Services(100m), Services(40m, 0m, VatCategoryCodes.Exempt, VatOperations.DisbursementExemption)]);
        List<VatOperation> operations = [.. new[] { mixed, export, intraGoods, construction, disbursement }.SelectMany(VatOperations.Of)];

        VatReturnResponse vat = VatReturnCalculator.Build(operations, Context(200m + 11m + 30m + 20m));

        Box(vat, "A1").Line.Should().Be("A1");
        Box(vat, "A1").BaseExclTax.Should().Be(1000m + 200m + 300m + 100m);
        Box(vat, "E1").BaseExclTax.Should().Be(800m);
        Box(vat, "E2").BaseExclTax.Should().Be(700m, "l'autoliquidation BTP va en E2, les débours nulle part");
        Box(vat, "F2").BaseExclTax.Should().Be(500m);
        Box(vat, "Rate20").Should().Match<VatReturnBoxResponse>(b => b.Line == "08" && b.BaseExclTax == 1100m && b.VatAmount == 220m);
        Box(vat, "Rate10").Should().Match<VatReturnBoxResponse>(b => b.Line == "9B" && b.BaseExclTax == 300m && b.VatAmount == 30m);
        Box(vat, "Rate55").Should().Match<VatReturnBoxResponse>(b => b.Line == "09" && b.BaseExclTax == 200m && b.VatAmount == 11m);
        vat.Boxes.Should().NotContain(b => b.Key == "Rate21", "le taux de 2,1 % n'apparaît que s'il sert");
        Box(vat, "OutOfScope").Sources.Should().ContainSingle().Which.PieceNumber.Should().Be("FAC-000005");

        vat.GrossVat.Should().Be(261m);
        Box(vat, "GrossTotal").Should().Match<VatReturnBoxResponse>(b => b.Line == "16" && b.VatAmount == 261m);
        Box(vat, "NetDue").Should().Match<VatReturnBoxResponse>(b => b.Line == "28" && b.VatAmount == 261m);
        vat.NetVatDue.Should().Be(261m);
        vat.VatCredit.Should().Be(0m);
        vat.Checks.Single(c => c.Code == VatReturnCalculator.CheckJournal).Severity.Should().Be(TaxCheckSeverityDto.Ok);
    }

    [Fact]
    public void Chaque_case_garde_les_pieces_qui_la_composent_et_les_avoirs_se_deduisent()
    {
        TaxDocument invoice = Invoice("FAC-000010", new(2026, 9, 5), [Services(1000m)]);
        TaxDocument credit = Invoice("AV-000001", new(2026, 9, 25), [Services(250m)], kind: InvoiceKind.CreditNote);
        List<VatOperation> operations = [.. VatOperations.Of(invoice), .. VatOperations.Of(credit)];

        VatReturnResponse vat = VatReturnCalculator.Build(operations, Context(150m));

        VatReturnBoxResponse rate20 = Box(vat, "Rate20");
        rate20.BaseExclTax.Should().Be(750m);
        rate20.VatAmount.Should().Be(150m);
        rate20.Sources.Select(s => s.PieceNumber).Should().Equal("FAC-000010", "AV-000001");
        rate20.Sources.Should().Contain(s => s.Kind == InvoiceKindDto.CreditNote && s.BaseExclTax == -250m && s.VatAmount == -50m);
        rate20.Sources.Sum(s => s.VatAmount).Should().Be(rate20.VatAmount, "la somme des pièces redonne la case");
    }

    [Fact]
    public void Un_ecart_avec_le_journal_bloque_et_les_brouillons_et_pieces_non_transmises_avertissent()
    {
        List<VatOperation> operations = [.. VatOperations.Of(Invoice("FAC-000020", new(2026, 9, 5), [Services(100m)]))];

        VatReturnResponse vat = VatReturnCalculator.Build(operations, Context(25m, drafts: ["Atelier Durand"], notTransmitted: ["FAC-000020"]));

        TaxCheckResponse journal = vat.Checks.Single(c => c.Code == VatReturnCalculator.CheckJournal);
        journal.Severity.Should().Be(TaxCheckSeverityDto.Blocking);
        journal.Expected.Should().Be(25m);
        journal.Actual.Should().Be(20m);
        vat.Checks.Single(c => c.Code == VatReturnCalculator.CheckDrafts).Should().Match<TaxCheckResponse>(c => c.Severity == TaxCheckSeverityDto.Warning && c.Count == 1);
        vat.Checks.Single(c => c.Code == VatReturnCalculator.CheckNotTransmitted).Pieces.Should().Equal("FAC-000020");
        vat.Checks.Single(c => c.Code == VatReturnCalculator.CheckDeductible).Severity.Should().Be(TaxCheckSeverityDto.Warning, "les achats ne sont pas encore dans Lettra");
    }

    [Fact]
    public void Les_services_sans_option_pour_les_debits_signalent_la_tva_sur_les_encaissements()
    {
        List<VatOperation> operations = [.. VatOperations.Of(Invoice("FAC-000030", new(2026, 9, 5), [Services(100m), Goods(50m)], vatOnDebits: false))];

        operations.Should().ContainSingle(o => o.DueOnReceipt).Which.Nature.Should().Be(RevenueNature.Services);
        VatReturnCalculator.Build(operations, Context(30m)).Checks
            .Single(c => c.Code == VatReturnCalculator.CheckVatOnReceipts).Pieces.Should().Equal("FAC-000030");
    }

    [Fact]
    public void Un_taux_inconnu_a_sa_propre_case_et_un_avertissement()
    {
        List<VatOperation> operations = [.. VatOperations.Of(Invoice("FAC-000040", new(2026, 9, 5), [Services(100m, 8.5m)]))];

        VatReturnResponse vat = VatReturnCalculator.Build(operations, Context(8.5m));

        vat.Boxes.Should().ContainSingle(b => b.Key == "RateOther").Which.RatePercent.Should().Be(8.5m);
        vat.Checks.Should().Contain(c => c.Code == VatReturnCalculator.CheckOtherRate && c.Severity == TaxCheckSeverityDto.Warning);
    }

    [Fact]
    public void La_ca12_n_a_pas_de_numero_de_ligne_et_un_credit_se_declare_en_case_25()
    {
        List<VatOperation> operations = [.. VatOperations.Of(Invoice("AV-000002", new(2026, 9, 5), [Services(100m)], kind: InvoiceKind.CreditNote))];

        VatReturnResponse ca12 = VatReturnCalculator.Build(operations, Context(-20m, VatReturnFormDto.Ca12));
        ca12.Boxes.Should().OnlyContain(b => b.Line == null, "les lignes de la 3517-S-SD restent à reporter à la relecture");
        ca12.VatCredit.Should().Be(20m);

        VatReturnResponse ca3 = VatReturnCalculator.Build(operations, Context(-20m));
        Box(ca3, "Credit").Should().Match<VatReturnBoxResponse>(b => b.Line == "25" && b.VatAmount == 20m);
        ca3.NetVatDue.Should().Be(0m);
    }

    [Fact]
    public void La_tva_d_un_taux_mixte_biens_et_services_redonne_celle_de_la_piece()
    {
        // 33,33 € de services et 66,67 € de biens à 20 % : 6,67 + 13,33 = 20,00, comme le récapitulatif imprimé.
        TaxDocument document = Invoice("FAC-000050", new(2026, 9, 5), [Services(33.33m), Goods(66.67m)]);

        IReadOnlyList<VatOperation> operations = VatOperations.Of(document);

        operations.Sum(o => o.VatAmount).Should().Be(document.Summaries.Sum(s => s.VatAmount));
        operations.Single(o => o.Nature == RevenueNature.Services).VatAmount.Should().Be(6.67m);
    }

    [Fact]
    public void Les_encaissements_remplacent_la_tva_des_services_due_a_l_encaissement()
    {
        TaxDocument document = Invoice("FAC-000060", new(2026, 8, 20), [Services(1000m)], vatOnDebits: false);
        IReadOnlyList<VatOperation> invoiced = VatOperations.Of(document);
        VatReceiptAllocation receipt = new(document.InvoiceId, InvoiceKind.Invoice, "FAC-000060", new(2026, 9, 10), Durand, "Atelier Durand", 20m, 500m, 100m);

        IReadOnlyList<VatOperation> operations = VatOperations.WithReceipts(invoiced, [receipt], new Dictionary<Guid, TaxDocument> { [document.InvoiceId] = document });

        operations.Should().ContainSingle().Which.Should().Match<VatOperation>(o => o.Date == new DateOnly(2026, 9, 10) && o.VatAmount == 100m && o.DueOnReceipt);
    }
}
