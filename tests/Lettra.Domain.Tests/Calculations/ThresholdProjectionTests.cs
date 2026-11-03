using Lettra.Calculations.Thresholds;

namespace Lettra.Domain.Tests.Calculations;

/// <summary>
/// La projection d'un seuil : cumul de la période, rythme des 90 derniers jours prolongé jusqu'au dernier jour, date de
/// franchissement constatée ou prévue, état (loin, à surveiller, prévu, franchi).
/// </summary>
public sealed class ThresholdProjectionTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);

    private static DatedAmount On(int month, int day, decimal amount) => new(new DateOnly(2026, month, day), amount);

    [Fact]
    public void Au_rythme_des_90_derniers_jours_le_franchissement_est_prevu_au_jour_pres()
    {
        // 9 000 € le 1er juin, rien avant : 9 000 / 90 = 100 € par jour ; il manque 11 000 € pour 20 000 €, soit 110 jours.
        ThresholdProgress progress = ThresholdProjection.Project([On(6, 1, 9_000m)], 20_000m, Start, End, new DateOnly(2026, 6, 30));

        progress.ToDate.Should().Be(9_000m);
        progress.DailyRate.Should().Be(100m);
        progress.Projected.Should().Be(27_400m, "9 000 + 100 × 184 jours restants");
        progress.Excess.Should().Be(7_400m);
        progress.CrossedOn.Should().BeNull();
        progress.ExpectedOn.Should().Be(new DateOnly(2026, 10, 19), "« plus de » 20 000 € : le 111e jour");
        progress.Status.Should().Be(ThresholdStatus.Projected);

        ThresholdProjection.Project([On(6, 1, 9_000m)], 20_000m, Start, End, new DateOnly(2026, 6, 30), inclusive: true)
            .ExpectedOn.Should().Be(new DateOnly(2026, 10, 18), "« au moins » 20 000 € : atteint le 110e jour");
    }

    [Fact]
    public void Le_franchissement_constate_garde_le_jour_du_dernier_passage_au_dessus_du_seuil()
    {
        DatedAmount[] amounts = [On(2, 1, 6_000m), On(3, 1, 5_000m), On(3, 15, -2_000m), On(4, 1, 3_000m)];

        ThresholdProgress progress = ThresholdProjection.Project(amounts, 10_000m, Start, End, new DateOnly(2026, 4, 10));

        progress.ToDate.Should().Be(12_000m);
        progress.CrossedOn.Should().Be(new DateOnly(2026, 4, 1), "l'avoir du 15 mars l'a fait redescendre");
        progress.ExpectedOn.Should().BeNull();
        progress.Status.Should().Be(ThresholdStatus.Crossed);
        ThresholdProjection.Project(amounts, 10_000m, Start, End, new DateOnly(2026, 3, 20)).Should()
            .Match<ThresholdProgress>(p => p.CrossedOn == null && p.ToDate == 9_000m, "sous le seuil après l'avoir");
    }

    [Fact]
    public void Une_projection_a_80_pourcent_du_seuil_est_a_surveiller_sinon_loin()
    {
        // 50 € par jour sur 90 jours ; 4 500 € à fin mars, 4 500 + 50 × 275 = 18 250 € en fin d'année.
        DatedAmount[] amounts = [On(1, 1, 4_500m)];
        DateOnly today = new(2026, 3, 31);

        ThresholdProjection.Project(amounts, 20_000m, Start, End, today).Status.Should().Be(ThresholdStatus.Near);
        ThresholdProjection.Project(amounts, 30_000m, Start, End, today).Status.Should().Be(ThresholdStatus.Far);
        ThresholdProjection.Project([], 0m, Start, End, today).Status.Should().Be(ThresholdStatus.Far, "un seuil nul ne s'approche pas");
    }

    [Fact]
    public void Le_rythme_se_lit_meme_avant_la_periode_et_ne_compte_pas_dans_le_cumul()
    {
        DatedAmount[] amounts = [new(new DateOnly(2025, 12, 1), 9_000m), On(1, 5, 900m)];

        ThresholdProgress progress = ThresholdProjection.Project(amounts, 50_000m, Start, End, new DateOnly(2026, 1, 10));

        progress.ToDate.Should().Be(900m, "décembre appartient à l'année passée");
        progress.DailyRate.Should().Be(110m, "(9 000 + 900) / 90");
    }

    [Fact]
    public void Les_sommes_sans_date_comptent_des_le_premier_jour_sans_entrer_dans_le_rythme()
    {
        ThresholdProgress progress = ThresholdProjection.Project([], 2_400m, Start, End, new DateOnly(2026, 5, 1), undated: 2_500m);

        progress.Should().Match<ThresholdProgress>(p => p.ToDate == 2_500m && p.DailyRate == 0m && p.CrossedOn == Start && p.Status == ThresholdStatus.Crossed);
        ThresholdProjection.Project([], 2_400m, Start, End, new DateOnly(2025, 12, 1), undated: 2_500m).CrossedOn
            .Should().BeNull("la période n'a pas commencé");
    }

    [Fact]
    public void Une_periode_finie_ne_se_projette_plus_et_un_rythme_negatif_compte_pour_zero()
    {
        ThresholdProgress closed = ThresholdProjection.Project([On(12, 1, 1_000m)], 5_000m, Start, End, new DateOnly(2027, 3, 1));
        closed.Should().Match<ThresholdProgress>(p => p.Projected == 1_000m && p.ExpectedOn == null && p.Status == ThresholdStatus.Far);

        ThresholdProgress refunds = ThresholdProjection.Project([On(1, 10, 8_000m), On(5, 1, -3_000m)], 10_000m, Start, End, new DateOnly(2026, 5, 10));
        refunds.DailyRate.Should().Be(0m);
        refunds.Projected.Should().Be(5_000m);
        refunds.Status.Should().Be(ThresholdStatus.Far);
    }

    [Fact]
    public void Un_franchissement_prevu_apres_la_fin_de_la_periode_n_est_pas_annonce()
    {
        // 90 € par jour, 1 000 € manquants : 12 jours, mais il n'en reste que 10.
        ThresholdProgress progress = ThresholdProjection.Project([On(12, 1, 8_100m)], 9_100m, Start, End, new DateOnly(2026, 12, 21));

        progress.ExpectedOn.Should().BeNull();
        progress.Status.Should().Be(ThresholdStatus.Near);
    }

    [Fact]
    public void Une_periode_qui_finit_avant_de_commencer_est_refusee()
    {
        FluentActions.Invoking(() => ThresholdProjection.Project([], 1m, End, Start, Start)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ThresholdProjection.Project(null!, 1m, Start, End, Start)).Should().Throw<ArgumentNullException>();
    }
}
