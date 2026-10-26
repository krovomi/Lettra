using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Missions;
using Lettra.Domain.Missions;

namespace Lettra.Application.Tests;

/// <summary>L'écran « Mission » : année de mission, honoraires acquis au prorata, taux horaire réalisé et répartition du temps.</summary>
public sealed class MissionOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Colleague = Guid.CreateVersion7();

    private static Mission Mission(DateOnly start, decimal fee = 3650m) =>
        Domain.Missions.Mission.Create(new(MissionKind.Presentation, MissionServices.Bookkeeping, start, fee, FeeSchedule.Monthly, 100m, "Claire Martin", null, null), Now).Value;

    private static TimeEntry Time(DateOnly date, int minutes, TimeTask task, Guid user) =>
        TimeEntry.Create(Guid.CreateVersion7(), date, user, minutes, task, null, date, Now).Value;

    [Fact]
    public void Les_honoraires_s_acquierent_au_prorata_des_jours_de_l_annee_de_mission()
    {
        Mission mission = Mission(new DateOnly(2025, 1, 1));

        MissionOverview.Earned(mission, new DateOnly(2025, 1, 1)).Should().Be(10m, "un jour sur 365");
        MissionOverview.Earned(mission, new DateOnly(2025, 12, 31)).Should().Be(3650m);
        MissionOverview.Earned(mission, new DateOnly(2026, 1, 10)).Should().Be(100m, "une nouvelle année de mission commence");
        MissionOverview.Earned(Mission(new DateOnly(2027, 1, 1)), new DateOnly(2026, 10, 26)).Should().Be(0m, "rien avant le début");
    }

    [Fact]
    public void Le_taux_horaire_realise_divise_l_acquis_par_les_heures_passees()
    {
        DateOnly today = new(2025, 1, 10);
        TimeEntry[] entries = [Time(today, 30, TimeTask.Vat, Colleague), Time(today.AddDays(-1), 30, TimeTask.Bookkeeping, Me), Time(today, 60, TimeTask.Vat, Me)];

        MissionOverviewResponse view = MissionOverview.Build(Mission(new DateOnly(2025, 1, 1)), today, entries,
            new CabinetPeople("Cabinet Durand", new Dictionary<Guid, string> { [Me] = "Jeanne Expert" }), Me);

        view.CabinetName.Should().Be("Cabinet Durand");
        view.YearStart.Should().Be(new DateOnly(2025, 1, 1));
        view.YearEnd.Should().Be(new DateOnly(2025, 12, 31));
        view.MinutesSpent.Should().Be(120);
        view.EarnedExclTax.Should().Be(100m);
        view.EffectiveHourlyRate.Should().Be(50m);
        view.ByTask.Should().Equal(new TimeTaskTotal(TimeTaskDto.Bookkeeping, 30), new TimeTaskTotal(TimeTaskDto.Vat, 90));
        view.Entries.Select(e => e.Date).Should().BeInDescendingOrder();
        view.Entries.Where(e => e.Mine).Should().OnlyContain(e => e.UserName == "Jeanne Expert");
        view.Entries.Single(e => !e.Mine).UserName.Should().BeNull("le collègue n'est pas dans le registre");
        view.Mission!.Services.Should().Equal(MissionServiceDto.Bookkeeping);
    }

    [Fact]
    public void Sans_mission_l_ecran_couvre_les_douze_derniers_mois_sans_taux()
    {
        DateOnly today = new(2026, 10, 26);

        MissionOverviewResponse view = MissionOverview.Build(null, today, [Time(today, 45, TimeTask.Advice, Me)], CabinetPeople.None, Me);

        view.Mission.Should().BeNull();
        view.YearStart.Should().Be(new DateOnly(2025, 10, 27));
        view.YearEnd.Should().Be(today);
        view.MinutesSpent.Should().Be(45);
        view.EarnedExclTax.Should().Be(0m);
        view.EffectiveHourlyRate.Should().BeNull();
    }

    [Fact]
    public void La_demande_de_l_ecran_devient_le_brouillon_du_domaine()
    {
        SaveMissionRequest request = new()
        {
            Kind = MissionKindDto.Review,
            Services = [MissionServiceDto.VatReturns, MissionServiceDto.Legal],
            StartDate = new DateOnly(2026, 4, 1),
            AnnualFeeExclTax = 1200m,
            Schedule = FeeScheduleDto.Quarterly,
            ClientSignatory = "Claire Martin",
        };

        Mission.Draft draft = request.ToDraft();

        draft.Kind.Should().Be(MissionKind.Review);
        draft.Services.Should().Be(MissionServices.VatReturns | MissionServices.Legal);
        draft.Schedule.Should().Be(FeeSchedule.Quarterly);
    }
}
