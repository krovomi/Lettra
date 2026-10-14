using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.SharedKernel;

public sealed class MoneyAndVatTests
{
    [Fact]
    public void Addition_dans_la_meme_devise() =>
        (Money.Eur(10.10m) + Money.Eur(0.015m)).RoundedToCents().Should().Be(Money.Eur(10.12m));

    [Fact]
    public void Addition_entre_devises_interdite()
    {
        Func<Money> act = () => Money.Eur(1m) + new Money(1m, "USD");

        act.Should().Throw<InvalidOperationException>().WithMessage("Money.CurrencyMismatch*");
    }

    [Theory]
    [InlineData("FR-20", 20)]
    [InlineData("FR-5.5", 5.5)]
    [InlineData("FR-2.1", 2.1)]
    public void Taux_de_tva_connus(string code, decimal percent) =>
        VatRate.FromCode(code).Value.Percent.Should().Be(percent);

    [Fact]
    public void Taux_de_tva_inconnu() =>
        VatRate.FromCode("FR-19.6").Errors.Should().ContainSingle().Which.Code.Should().Be(SharedKernelErrors.VatRateUnknownCode);
}
