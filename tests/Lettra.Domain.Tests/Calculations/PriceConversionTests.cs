using Lettra.Calculations;

namespace Lettra.Domain.Tests.Calculations;

public sealed class PriceConversionTests
{
    [Theory]
    [InlineData(100, 20, 120)]
    [InlineData(85.5, 5.5, 90.20)]
    [InlineData(0.125, 20, 0.15)]
    [InlineData(10, 0, 10)]
    public void Le_prix_ttc_est_arrondi_au_centime(decimal exclTax, decimal rate, decimal expected) =>
        PriceConversion.InclTax(exclTax, rate).Should().Be(expected);

    [Theory]
    [InlineData(120, 20)]
    [InlineData(99.99, 20)]
    [InlineData(19.90, 5.5)]
    [InlineData(1, 2.1)]
    [InlineData(0.01, 20)]
    public void Le_prix_ht_deduit_redonne_exactement_le_ttc_saisi(decimal inclTax, decimal rate)
    {
        decimal exclTax = PriceConversion.ExclTaxFromInclTax(inclTax, rate);

        decimal.Round(exclTax, 4).Should().Be(exclTax);
        PriceConversion.InclTax(exclTax, rate).Should().Be(inclTax);
    }

    [Fact]
    public void Un_taux_hors_bornes_est_refuse() =>
        FluentActions.Invoking(() => PriceConversion.InclTax(10m, 120m)).Should().Throw<ArgumentOutOfRangeException>();
}
