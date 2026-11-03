using Lettra.Application.Abstractions.Cabinet;
using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Calculations.Thresholds;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;

namespace Lettra.Exports.Tests.Tax;

/// <summary>
/// Les seuils qui approchent pour un dossier : quels seuils s'appliquent selon la fiche entreprise, leur projection, la
/// date à laquelle les règles changent, l'ordre d'urgence et le compteur de la vue cabinet.
/// </summary>
public sealed class ThresholdsTests
{
    private static ThresholdInput Input(
        IReadOnlyList<SalesAmount> sales,
        bool franchise = false,
        VatDeclarationRegime? regime = VatDeclarationRegime.RealNormal,
        CompanySize size = CompanySize.Unknown,
        FiscalSettings? settings = null,
        IReadOnlyList<PartyAmounts>? suppliers = null,
        IReadOnlyList<PartyAmounts>? beneficiaries = null,
        bool profile = true) =>
        new(profile, franchise, regime, size, settings ?? FiscalSettings.Default, sales, suppliers ?? [], beneficiaries ?? []);

    private static SalesAmount Services(int year, int month, int day, decimal amount) => new(new DateOnly(year, month, day), RevenueNature.Services, amount);

    private static SalesAmount Goods(int year, int month, int day, decimal amount) => new(new DateOnly(year, month, day), RevenueNature.Goods, amount);

    [Fact]
    public void En_franchise_les_deux_seuils_des_services_sont_prevus_avec_leur_date_d_effet()
    {
        // 4 000 € de services le 15 de chaque mois : 36 000 € au 15 septembre, 12 000 € sur les 90 derniers jours.
        SalesAmount[] sales = [.. Enumerable.Range(1, 9).Select(month => Services(2026, month, 15, 4_000m))];
        DateOnly today = new(2026, 9, 15);

        IReadOnlyList<ThresholdWatchResponse> rows = ThresholdWatch.Evaluate(Input(sales, franchise: true, regime: null, size: CompanySize.Micro), today);

        rows.Select(r => r.Code).Should().Equal(ThresholdCodes.FranchiseServicesBase, ThresholdCodes.FranchiseServicesIncreased, ThresholdCodes.SizeMicro);
        rows[0].Should().Match<ThresholdWatchResponse>(r => r.Threshold == 37_500m && r.Tier == ThresholdTierDto.Base && r.Status == ThresholdStatusDto.Projected
            && r.ExpectedOn == new DateOnly(2026, 9, 27) && r.EffectiveOn == new DateOnly(2027, 1, 1) && r.Soon && r.ToDate == 36_000m && r.Projected == 50_266.67m);
        rows[1].Should().Match<ThresholdWatchResponse>(r => r.Threshold == 41_250m && r.Tier == ThresholdTierDto.Increased
            && r.ExpectedOn == new DateOnly(2026, 10, 25) && r.EffectiveOn == new DateOnly(2026, 10, 25) && r.Soon && r.Excess == 9_016.67m);
        rows[2].Should().Match<ThresholdWatchResponse>(r => r.Status == ThresholdStatusDto.Far && r.EffectiveOn == null && !r.Soon
            && r.Family == ThresholdFamilyDto.CompanySize && r.Subject == ThresholdCodes.SizeMicro && r.PartyId == null);
        rows.Should().OnlyContain(r => r.Basis == ThresholdBasisDto.ExclTax && r.PeriodStart == new DateOnly(2026, 1, 1) && r.PeriodEnd == new DateOnly(2026, 12, 31)
            && r.Source.Length > 0);

        ThresholdWatch.Indicator(new ThresholdsResponse(today, 90, 3, false, rows))
            .Should().Be(new DossierIndicator(DossierIndicatorKeys.Thresholds, 2, IndicatorLevel.Warning, new DateOnly(2026, 9, 27)));
    }

    [Fact]
    public void Au_regime_simplifie_le_seuil_majore_franchi_fait_passer_au_reel_depuis_le_debut_de_l_annee()
    {
        SalesAmount[] sales = [Services(2026, 2, 1, 200_000m), Services(2026, 5, 1, 150_000m)];
        DateOnly today = new(2026, 6, 30);

        IReadOnlyList<ThresholdWatchResponse> rows = ThresholdWatch.Evaluate(Input(sales, regime: VatDeclarationRegime.RealSimplified), today);

        rows.Select(r => r.Code).Should().Equal(ThresholdCodes.SimplifiedServicesIncreased, ThresholdCodes.QuarterlyBase);
        rows[0].Should().Match<ThresholdWatchResponse>(r => r.Status == ThresholdStatusDto.Crossed && r.CrossedOn == new DateOnly(2026, 5, 1)
            && r.EffectiveOn == new DateOnly(2026, 1, 1) && r.Threshold == 323_000m && r.Soon);
        rows[1].Should().Match<ThresholdWatchResponse>(r => r.Status == ThresholdStatusDto.Far && r.Threshold == 1_000_000m,
            "le seuil de la déclaration trimestrielle de 2027 se lit sur le chiffre d'affaires de 2026");
        ThresholdWatch.Indicator(new ThresholdsResponse(today, 90, 3, false, rows))
            .Should().Be(new DossierIndicator(DossierIndicatorKeys.Thresholds, 1, IndicatorLevel.Critical, new DateOnly(2026, 5, 1)));

        // Un exercice décalé : le réel normal s'applique depuis le premier jour de l'exercice en cours.
        FiscalSettings june = FiscalSettings.Default with { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 };
        ThresholdWatch.Evaluate(Input(sales, regime: VatDeclarationRegime.RealSimplified, settings: june), today)[0].EffectiveOn
            .Should().Be(new DateOnly(2025, 7, 1));
    }

    [Fact]
    public void Au_trimestre_le_seuil_majore_rend_la_tva_mensuelle_des_le_mois_du_depassement()
    {
        SalesAmount[] sales = [Goods(2027, 1, 15, 900_000m), Goods(2027, 3, 1, 300_000m)];
        FiscalSettings quarterly = FiscalSettings.Default with { VatPeriodicity = VatPeriodicityDto.Quarterly };
        DateOnly today = new(2027, 3, 31);

        IReadOnlyList<ThresholdWatchResponse> rows = ThresholdWatch.Evaluate(Input(sales, settings: quarterly), today);

        rows.Select(r => (r.Code, r.Status, r.EffectiveOn)).Should().Equal(
            (ThresholdCodes.QuarterlyBase, ThresholdStatusDto.Crossed, new DateOnly(2028, 1, 1)),
            (ThresholdCodes.QuarterlyIncreased, ThresholdStatusDto.Crossed, new DateOnly(2027, 3, 1)));
        ThresholdWatch.Indicator(new ThresholdsResponse(today, 90, 3, false, rows))!.Level.Should().Be(IndicatorLevel.Critical);
        ThresholdWatch.Evaluate(Input(sales), today).Should().BeEmpty("un dossier au réel mensuel n'a pas ce seuil");
    }

    [Fact]
    public void En_franchise_un_vendeur_de_biens_compare_tout_son_chiffre_d_affaires_au_seuil_des_ventes()
    {
        SalesAmount[] sales = [Goods(2026, 3, 1, 50_000m), Services(2026, 3, 2, 10_000m)];

        IReadOnlyList<ThresholdWatchResponse> rows = ThresholdWatch.Evaluate(Input(sales, franchise: true, regime: null), new DateOnly(2026, 3, 31));

        rows.Select(r => r.Code).Should().BeEquivalentTo(
            ThresholdCodes.FranchiseServicesIncreased, ThresholdCodes.FranchiseServicesBase, ThresholdCodes.FranchiseGoodsIncreased, ThresholdCodes.FranchiseGoodsBase);
        rows.Single(r => r.Code == ThresholdCodes.FranchiseGoodsIncreased).ToDate.Should().Be(60_000m);
        rows.Single(r => r.Code == ThresholdCodes.FranchiseServicesIncreased).ToDate.Should().Be(10_000m);
        ThresholdWatch.Evaluate(Input([Goods(2026, 3, 1, 50_000m)], franchise: true, regime: null), new DateOnly(2026, 3, 31))
            .Select(r => r.Code).Should().BeEquivalentTo(ThresholdCodes.FranchiseGoodsIncreased, ThresholdCodes.FranchiseGoodsBase);
        ThresholdWatch.Evaluate(Input([], franchise: true, regime: null), new DateOnly(2026, 3, 31))
            .Select(r => r.Code).Should().BeEquivalentTo<string>([ThresholdCodes.FranchiseServicesIncreased, ThresholdCodes.FranchiseServicesBase], "sans vente, les services par défaut");
    }

    [Fact]
    public void Les_tiers_n_apparaissent_qu_a_l_approche_de_leur_seuil()
    {
        Guid mason = Guid.Parse("0199a000-0000-7000-8000-0000000000c1");
        Guid lawyer = Guid.Parse("0199a000-0000-7000-8000-0000000000c2");
        PartyAmounts[] suppliers =
        [
            new(mason, "Maçonnerie Petit", [new(new DateOnly(2026, 6, 1), 4_800m)]),
            new(Guid.CreateVersion7(), "Papeterie", [new(new DateOnly(2026, 6, 1), 100m)]),
        ];
        PartyAmounts[] beneficiaries = [new(lawyer, "Cabinet Durand", [], Undated: 2_500m), new(Guid.CreateVersion7(), "Atelier Roux", [])];

        IReadOnlyList<ThresholdWatchResponse> rows = ThresholdWatch.Evaluate(
            Input([], profile: false, regime: null, size: CompanySize.Micro, suppliers: suppliers, beneficiaries: beneficiaries), new DateOnly(2026, 6, 30));

        rows.Should().HaveCount(2, "sans fiche entreprise, ni TVA ni catégorie ; les tiers loin du seuil sont tus");
        rows[0].Should().Match<ThresholdWatchResponse>(r => r.Code == ThresholdCodes.FeeReturn && r.PartyId == lawyer && r.PartyName == "Cabinet Durand"
            && r.Subject == lawyer.ToString() && r.Status == ThresholdStatusDto.Crossed && r.CrossedOn == new DateOnly(2026, 1, 1)
            && r.EffectiveOn == null && r.Basis == ThresholdBasisDto.InclTax && r.Threshold == 2_400m);
        rows[1].Should().Match<ThresholdWatchResponse>(r => r.Code == ThresholdCodes.Vigilance && r.PartyId == mason
            && r.Status == ThresholdStatusDto.Projected && r.ExpectedOn == new DateOnly(2026, 7, 4) && r.EffectiveOn == new DateOnly(2026, 7, 4));
        ThresholdWatch.Evaluate(Input([], suppliers: [new(mason, "Maçonnerie Petit", [new(new DateOnly(2026, 6, 1), 5_000m)])]), new DateOnly(2026, 6, 30))
            .Single().Status.Should().Be(ThresholdStatusDto.Crossed, "l'attestation est due à partir de 5 000 € atteints");
    }

    [Fact]
    public void Le_chiffre_d_affaires_compte_biens_et_services_hors_debours_et_acomptes()
    {
        DateOnly date = new(2026, 3, 1);
        TaxLine[] lines =
        [
            TaxSamples.Services(1_000m),
            TaxSamples.Services(200m),
            TaxSamples.Goods(500m),
            TaxSamples.Services(50m, 0m, VatCategoryCodes.Exempt, VatOperations.DisbursementExemption),
            new(RevenueNature.DownPayment, VatCategoryCodes.Standard, 20m, null, 300m),
        ];

        ThresholdWatch.Sales(TaxSamples.Invoice("FAC-1", date, lines)).Should().BeEquivalentTo<SalesAmount>(
            [new(date, RevenueNature.Services, 1_200m), new(date, RevenueNature.Goods, 500m)]);
        ThresholdWatch.Sales(TaxSamples.Invoice("AV-1", date, [TaxSamples.Services(100m)], kind: InvoiceKind.CreditNote))
            .Should().Equal(new SalesAmount(date, RevenueNature.Services, -100m));
        FluentActions.Invoking(() => ThresholdWatch.Sales(null!).ToList()).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rien_de_proche_ne_donne_pas_d_indicateur()
    {
        ThresholdWatch.Indicator(new ThresholdsResponse(new DateOnly(2026, 6, 30), 90, 3, false, [])).Should().BeNull();
        FluentActions.Invoking(() => ThresholdWatch.Indicator(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ThresholdWatch.DueOn(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ThresholdWatch.Evaluate(null!, new DateOnly(2026, 6, 30))).Should().Throw<ArgumentNullException>();
    }
}
