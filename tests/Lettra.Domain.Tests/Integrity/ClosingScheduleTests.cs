using Lettra.Domain.Integrity;

namespace Lettra.Domain.Tests.Integrity;

public sealed class ClosingScheduleTests
{
    private static readonly ClosingSchedule Schedule = ClosingSchedule.Paris;

    [Fact]
    public void Un_journal_vide_n_a_rien_a_cloturer() =>
        Schedule.DueMonths(null, null, DateTimeOffset.UtcNow).Should().BeEmpty();

    [Fact]
    public void Chaque_mois_ecoule_depuis_la_premiere_entree_est_cloture_meme_vide()
    {
        IReadOnlyList<ClosingPeriod> due = Schedule.DueMonths(null,
            new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));

        due.Select(p => (p.Start, p.End)).Should().Equal(
            (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        due[0].EndsAt.Should().Be(new DateTimeOffset(2026, 1, 31, 23, 0, 0, TimeSpan.Zero), "minuit à Paris en hiver");
        due.Should().OnlyContain(p => p.Kind == ClosingPeriodKind.Month);
    }

    [Fact]
    public void Le_mois_est_celui_de_Paris_pas_celui_d_UTC()
    {
        // 31 mars 22 h 30 UTC = 1er avril 0 h 30 à Paris (heure d'été) : la première clôture est celle d'avril.
        IReadOnlyList<ClosingPeriod> due = Schedule.DueMonths(null,
            new DateTimeOffset(2026, 3, 31, 22, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero));

        due.Should().ContainSingle().Which.Start.Should().Be(new DateOnly(2026, 4, 1));
        due[0].EndsAt.Should().Be(new DateTimeOffset(2026, 4, 30, 22, 0, 0, TimeSpan.Zero), "minuit à Paris en été");
    }

    [Fact]
    public void On_repart_du_lendemain_de_la_derniere_cloture()
    {
        IReadOnlyList<ClosingPeriod> due = Schedule.DueMonths(new DateOnly(2026, 2, 28),
            new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));

        due.Should().ContainSingle().Which.Start.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Le_mois_en_cours_n_est_pas_cloture_avant_minuit_le_dernier_jour()
    {
        DateTimeOffset endOfJanuary = new(2026, 1, 31, 23, 0, 0, TimeSpan.Zero);
        DateTimeOffset firstEntry = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        Schedule.DueMonths(null, firstEntry, endOfJanuary.AddTicks(-1)).Should().BeEmpty();
        Schedule.DueMonths(null, firstEntry, endOfJanuary).Should().ContainSingle();
    }
}
