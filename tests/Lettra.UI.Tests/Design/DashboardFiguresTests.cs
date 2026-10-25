using Lettra.Contracts.Billing;
using Lettra.UI.Shared.Billing;

namespace Lettra.UI.Tests.Design;

/// <summary>Ce que l'écran d'accueil tire des chiffres du serveur (le calcul lui-même : BillingDashboardTests).</summary>
public sealed class DashboardFiguresTests
{
    private static DashboardFigures Figures(params decimal[] months) => new(
        new BillingDashboardResponse([.. months.Select((total, i) => new MonthRevenueDto(new DateOnly(2026, 5 + i, 1), total))], 0, 0, 0m),
        [], []);

    [Fact]
    public void L_evolution_compare_le_mois_en_cours_au_mois_precedent()
    {
        DashboardFigures chiffres = Figures(0m, 0m, 0m, 0m, 500m, 800m);

        chiffres.ThisMonth.Should().Be(800m);
        chiffres.PreviousMonth.Should().Be(500m);
        chiffres.Trend.Should().Be(60m);
    }

    [Fact]
    public void Sans_facture_le_mois_precedent_il_n_y_a_pas_d_evolution() =>
        Figures(0m, 0m, 0m, 0m, 0m, 1000m).Trend.Should().BeNull();
}
