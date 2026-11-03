using Lettra.Calculations.Thresholds;
using Lettra.Domain.Parties;
using Lettra.Domain.Vigilance;

namespace Lettra.Domain.Tests.Calculations;

/// <summary>Le catalogue daté des seuils : chaque montant à sa date, une seule valeur par jour, une source pour chacun.</summary>
public sealed class ThresholdCatalogTests
{
    [Theory]
    [InlineData(ThresholdCodes.FranchiseGoodsBase, "2024-12-31", 91_900)]
    [InlineData(ThresholdCodes.FranchiseGoodsBase, "2025-01-01", 85_000)]
    [InlineData(ThresholdCodes.FranchiseGoodsIncreased, "2026-06-30", 93_500)]
    [InlineData(ThresholdCodes.FranchiseServicesBase, "2024-06-30", 36_800)]
    [InlineData(ThresholdCodes.FranchiseServicesBase, "2027-01-01", 37_500)]
    [InlineData(ThresholdCodes.FranchiseServicesIncreased, "2026-06-30", 41_250)]
    [InlineData(ThresholdCodes.SimplifiedGoodsIncreased, "2025-12-31", 925_000)]
    [InlineData(ThresholdCodes.SimplifiedGoodsIncreased, "2026-01-01", 1_040_000)]
    [InlineData(ThresholdCodes.SimplifiedServicesIncreased, "2026-12-31", 323_000)]
    [InlineData(ThresholdCodes.QuarterlyBase, "2027-01-01", 1_000_000)]
    [InlineData(ThresholdCodes.QuarterlyIncreased, "2027-06-30", 1_100_000)]
    [InlineData(ThresholdCodes.SizeMicro, "2026-06-30", 2_000_000)]
    [InlineData(ThresholdCodes.SizeSmallOrMedium, "2026-06-30", 50_000_000)]
    [InlineData(ThresholdCodes.SizeIntermediate, "2026-06-30", 1_500_000_000)]
    [InlineData(ThresholdCodes.Vigilance, "2015-04-30", 3_000)]
    [InlineData(ThresholdCodes.Vigilance, "2015-05-01", 5_000)]
    [InlineData(ThresholdCodes.FeeReturn, "2023-12-31", 1_200)]
    [InlineData(ThresholdCodes.FeeReturn, "2024-01-01", 2_400)]
    public void Chaque_seuil_a_son_montant_a_sa_date(string code, string date, decimal amount) =>
        ThresholdCatalog.AmountOn(code, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(amount);

    [Fact]
    public void Le_regime_simplifie_s_arrete_fin_2026_et_la_declaration_trimestrielle_commence_en_2027()
    {
        ThresholdCatalog.Find(ThresholdCodes.SimplifiedServicesIncreased, new DateOnly(2027, 1, 1)).Should().BeNull();
        ThresholdCatalog.Find(ThresholdCodes.QuarterlyBase, new DateOnly(2026, 12, 31)).Should().BeNull();
        ThresholdCatalog.Find(ThresholdCodes.FranchiseGoodsBase, new DateOnly(2022, 12, 31)).Should().BeNull("le catalogue commence en 2023");
        FluentActions.Invoking(() => ThresholdCatalog.AmountOn(ThresholdCodes.QuarterlyBase, new DateOnly(2026, 6, 30)))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ThresholdCatalog.AmountOn("inconnu", new DateOnly(2026, 6, 30))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Un_seul_montant_par_jour_et_une_source_pour_chacun()
    {
        foreach (IGrouping<string, ThresholdParameter> code in ThresholdCatalog.All.GroupBy(p => p.Code))
        {
            ThresholdParameter[] periods = [.. code.OrderBy(p => p.ValidFrom)];
            for (int i = 1; i < periods.Length; i++)
            {
                periods[i - 1].ValidUntil.Should().NotBeNull($"{code.Key} : une période close avant la suivante");
                periods[i - 1].ValidUntil!.Value.Should().BeBefore(periods[i].ValidFrom, $"{code.Key} : deux montants le même jour");
            }
        }

        ThresholdCatalog.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Source) && p.Amount > 0m && (p.ValidUntil == null || p.ValidUntil >= p.ValidFrom));
    }

    [Fact]
    public void Les_seuils_des_honoraires_et_de_la_vigilance_viennent_du_catalogue()
    {
        FeeBeneficiary.ThresholdFor(new DateOnly(2025, 12, 31)).Should().Be(2_400m);
        FeeBeneficiary.ThresholdFor(new DateOnly(2023, 12, 31)).Should().Be(1_200m);
        SubcontractorAttestation.Threshold.Should().Be(5_000m);
    }
}
