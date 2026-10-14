using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Tests;

public sealed class EscalationTests
{
    private static readonly EscalationPolicy Policy = new(BusinessCalendar.Paris);

    // Mardi 14/10/2026 10:00 à Paris (UTC+2).
    private static readonly DateTimeOffset MardiDixHeures = new(2026, 10, 14, 10, 0, 0, TimeSpan.FromHours(2));

    private static Alert Raise(Severity severity, DateTimeOffset at) =>
        Alert.Raise("r", "Règle", severity, AlertCategory.Availability, AlertScope.ForCell("mut-fr-01").Value, at).Value;

    [Fact]
    public void Une_S1_non_prise_en_charge_monte_au_niveau_1_a_15_minutes_puis_au_niveau_2_a_30_minutes()
    {
        Alert alert = Raise(Severity.S1, MardiDixHeures);

        Policy.Apply(alert, MardiDixHeures.AddMinutes(14)).Should().BeNull();
        Policy.Apply(alert, MardiDixHeures.AddMinutes(15)).Should().Be(1);
        Policy.Apply(alert, MardiDixHeures.AddMinutes(20)).Should().BeNull("le niveau 1 est déjà atteint");
        Policy.Apply(alert, MardiDixHeures.AddMinutes(30)).Should().Be(2);
        alert.EscalationLevel.Should().Be(2);
    }

    [Fact]
    public void Une_S1_prise_en_charge_n_escalade_plus()
    {
        Alert alert = Raise(Severity.S1, MardiDixHeures);
        alert.Acknowledge(Actor.Human(Guid.CreateVersion7()), MardiDixHeures.AddMinutes(5));

        Policy.Apply(alert, MardiDixHeures.AddHours(2)).Should().BeNull();
    }

    [Fact]
    public void Une_S1_compte_24_heures_sur_24_meme_la_nuit_du_samedi()
    {
        DateTimeOffset samediTroisHeures = new(2026, 10, 17, 3, 0, 0, TimeSpan.FromHours(2));

        Policy.TakeoverDue(Raise(Severity.S1, samediTroisHeures)).Should().Be(samediTroisHeures.AddMinutes(15));
    }

    [Fact]
    public void Une_S2_du_vendredi_soir_est_due_lundi_en_heures_ouvrees()
    {
        DateTimeOffset vendrediDixSeptHeures = new(2026, 10, 16, 17, 0, 0, TimeSpan.FromHours(2));

        // 1 h vendredi (17-18 h), puis 3 h lundi à partir de 9 h.
        Policy.TakeoverDue(Raise(Severity.S2, vendrediDixSeptHeures))
            .Should().Be(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Une_S3_est_due_deux_jours_ouvres_plus_tard_et_une_S4_jamais()
    {
        Policy.TakeoverDue(Raise(Severity.S3, MardiDixHeures))
            .Should().Be(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(2)));
        Policy.TakeoverDue(Raise(Severity.S4, MardiDixHeures)).Should().BeNull();
        Policy.StepsFor(Raise(Severity.S3, MardiDixHeures)).Should().BeEmpty();
    }

    [Fact]
    public void Le_passage_a_l_heure_d_hiver_ne_decale_pas_les_heures_ouvrees()
    {
        // Vendredi 23/10/2026 16:00 (UTC+2) ; changement d'heure dans la nuit du 24 au 25.
        DateTimeOffset vendredi = new(2026, 10, 23, 16, 0, 0, TimeSpan.FromHours(2));

        Policy.TakeoverDue(Raise(Severity.S2, vendredi))
            .Should().Be(new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.FromHours(1)));
    }
}
