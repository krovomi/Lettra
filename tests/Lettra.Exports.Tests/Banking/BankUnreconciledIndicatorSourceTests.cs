using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Tests.Banking;

public sealed class BankUnreconciledIndicatorSourceTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static DossierIndicator Of(int pending, DateOnly? oldest) =>
        BankUnreconciledIndicatorSource.Of(new BankReconciliationStatus(null, null, pending, oldest, null), Today);

    [Fact]
    public void Sans_operation_a_rapprocher_le_compteur_est_a_zero()
    {
        Of(0, null).Should().Be(new DossierIndicator(DossierIndicatorKeys.BankUnreconciled, 0, IndicatorLevel.Ok));
    }

    [Theory]
    [InlineData(3, IndicatorLevel.Info)]
    [InlineData(15, IndicatorLevel.Info)]
    [InlineData(16, IndicatorLevel.Warning)]
    [InlineData(30, IndicatorLevel.Warning)]
    [InlineData(31, IndicatorLevel.Critical)]
    public void L_urgence_suit_l_age_de_la_plus_ancienne_operation(int days, IndicatorLevel expected)
    {
        DossierIndicator indicator = Of(4, Today.AddDays(-days));

        indicator.Count.Should().Be(4);
        indicator.Level.Should().Be(expected);
        indicator.DueOn.Should().Be(Today.AddDays(-days));
    }
}
