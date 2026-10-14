using Lettra.Calculations;

namespace Lettra.Domain.Tests.Calculations;

public sealed class RoundingTests
{
    [Theory]
    [InlineData("0.125", "0.13")]
    [InlineData("0.124", "0.12")]
    [InlineData("-0.125", "-0.13")]
    [InlineData("2.675", "2.68")]
    [InlineData("10", "10.00")]
    public void Arrondi_au_centime_les_demis_s_eloignent_de_zero(string amount, string expected) =>
        Rounding.ToCents(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Montant_de_ligne_applique_la_remise_puis_arrondit()
    {
        decimal total = LineAmounts.TotalExclTax(new LineInput(Quantity: 3m, UnitPriceExclTax: 19.99m, VatRatePercent: 20m, DiscountPercent: 10m));

        total.Should().Be(53.97m); // 59,97 × 0,9 = 53,973
    }

    [Fact]
    public void Remise_hors_bornes_est_refusee()
    {
        Action act = () => LineAmounts.TotalExclTax(new LineInput(1m, 10m, 20m, DiscountPercent: 120m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void La_TVA_est_calculee_par_taux_sur_la_base_cumulee()
    {
        // Trois lignes à 0,10 € HT à 20 % : TVA ligne à ligne = 3 × 0,02 = 0,06 ; par taux = 0,30 × 20 % = 0,06.
        // Deux lignes à 0,125 € : base 0,26 (0,13 + 0,13) ; TVA à 5,5 % = 0,0143 → 0,01.
        DocumentTotals totals = DocumentCalculator.Compute(
        [
            new LineInput(1m, 0.10m, 20m),
            new LineInput(1m, 0.10m, 20m),
            new LineInput(1m, 0.10m, 20m),
            new LineInput(1m, 0.125m, 5.5m),
            new LineInput(1m, 0.125m, 5.5m),
        ]);

        totals.Breakdown.Should().Equal(
            new VatBreakdownLine(20m, 0.30m, 0.06m),
            new VatBreakdownLine(5.5m, 0.26m, 0.01m));
        totals.TotalExclTax.Should().Be(0.56m);
        totals.TotalVat.Should().Be(0.07m);
        totals.TotalInclTax.Should().Be(0.63m);
    }

    [Fact]
    public void Document_vide_donne_des_totaux_nuls()
    {
        DocumentTotals totals = DocumentCalculator.Compute([]);

        totals.TotalInclTax.Should().Be(0m);
        totals.Breakdown.Should().BeEmpty();
    }
}
