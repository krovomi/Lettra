using System.Globalization;
using Lettra.Domain.Billing;

namespace Lettra.Domain.Tests.Billing;

/// <summary>Plafond légal des délais de paiement entre professionnels et tranches de retard du rapport de gestion.</summary>
public sealed class PaymentDelaysTests
{
    [Theory]
    [InlineData("2026-01-15", "2026-03-31")] // 45 jours puis fin de mois l'emporte
    [InlineData("2026-01-31", "2026-04-01")] // 60 jours l'emporte en fin de mois
    [InlineData("2026-02-20", "2026-04-30")]
    [InlineData("2026-12-10", "2027-02-14")] // fin de mois puis 45 jours l'emporte
    public void Le_plafond_est_la_plus_tardive_des_lectures_admises(string issue, string ceiling) =>
        PaymentDelays.LegalCeiling(DateOnly.Parse(issue, CultureInfo.InvariantCulture)).Should().Be(DateOnly.Parse(ceiling, CultureInfo.InvariantCulture));

    [Fact]
    public void Le_depassement_se_compte_en_jours_apres_le_plafond()
    {
        DateOnly issue = new(2026, 1, 15);

        PaymentDelays.DaysOverCeiling(issue, new DateOnly(2026, 3, 31)).Should().Be(0);
        PaymentDelays.DaysOverCeiling(issue, new DateOnly(2026, 2, 14)).Should().Be(0);
        PaymentDelays.DaysOverCeiling(issue, new DateOnly(2026, 4, 30)).Should().Be(30);
    }

    [Fact]
    public void Le_retard_est_nul_le_jour_de_l_echeance_et_negatif_avant()
    {
        DateOnly due = new(2026, 3, 31);

        PaymentDelays.DaysLate(due, due).Should().Be(0);
        PaymentDelays.DaysLate(due, new DateOnly(2026, 3, 1)).Should().Be(-30);
        PaymentDelays.DaysLate(due, new DateOnly(2026, 4, 30)).Should().Be(30);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(60, 2)]
    [InlineData(61, 3)]
    [InlineData(90, 3)]
    [InlineData(91, 4)]
    [InlineData(400, 4)]
    public void Les_tranches_suivent_le_modele_du_rapport_de_gestion(int daysLate, int bracket) =>
        PaymentDelays.Bracket(daysLate).Should().Be(bracket);

    [Fact]
    public void Une_facture_non_echue_n_a_pas_de_tranche()
    {
        Action act = () => PaymentDelays.Bracket(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
