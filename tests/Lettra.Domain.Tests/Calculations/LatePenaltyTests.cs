using Lettra.Calculations;

namespace Lettra.Domain.Tests.Calculations;

public sealed class LatePenaltyTests
{
    [Fact]
    public void Un_professionnel_doit_les_interets_au_prorata_et_l_indemnite_de_40_euros()
    {
        // 1 000 € à 12 % pendant 30 jours : 1000 × 0,12 × 30 / 365 = 9,863… → 9,86 €.
        LatePenalty penalty = LatePenaltyCalculator.Compute(1000m, 12m, 30, professionalCustomer: true);

        penalty.Should().Be(new LatePenalty(9.86m, 40m));
        penalty.Total.Should().Be(49.86m);
    }

    [Fact]
    public void Un_particulier_ne_doit_pas_l_indemnite_forfaitaire() =>
        LatePenaltyCalculator.Compute(1000m, 12m, 30, professionalCustomer: false).Should().Be(new LatePenalty(9.86m, 0m));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Sans_taux_sur_la_facture_il_n_y_a_pas_d_interets(int? rate) =>
        LatePenaltyCalculator.Compute(500m, rate, 45, professionalCustomer: true).Should().Be(new LatePenalty(0m, 40m));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(100, 0)]
    public void Rien_n_est_du_sans_reste_ni_retard(decimal outstanding, int days) =>
        LatePenaltyCalculator.Compute(outstanding, 12m, days, professionalCustomer: true).Should().Be(default(LatePenalty));
}
