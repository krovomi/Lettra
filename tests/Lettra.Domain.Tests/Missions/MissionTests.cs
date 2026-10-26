using System.Globalization;
using Lettra.Domain.Common;
using Lettra.Domain.Missions;

namespace Lettra.Domain.Tests.Missions;

public sealed class MissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static Mission.Draft Draft(decimal fee = 2400m, FeeSchedule schedule = FeeSchedule.Monthly, decimal? rate = 80m, DateOnly? start = null) =>
        new(MissionKind.Presentation, MissionServices.Bookkeeping | MissionServices.AnnualAccounts, start ?? new DateOnly(2026, 1, 1), fee, schedule, rate,
            "  Claire Martin ", " ", null);

    [Fact]
    public void Le_forfait_se_partage_selon_le_rythme_et_le_taux_vise_donne_le_budget_d_heures()
    {
        Mission monthly = Mission.Create(Draft(), Now).Value;
        Mission quarterly = Mission.Create(Draft(1000m, FeeSchedule.Quarterly, rate: 0m), Now).Value;

        monthly.Id.Should().Be(Mission.SingletonId);
        monthly.FeePerInstalment.Should().Be(200m);
        monthly.BudgetHours.Should().Be(30m);
        monthly.ClientSignatory.Should().Be("Claire Martin");
        monthly.Manager.Should().BeNull();
        quarterly.FeePerInstalment.Should().Be(250m);
        quarterly.TargetHourlyRate.Should().BeNull("un taux nul veut dire « pas de taux visé »");
        quarterly.BudgetHours.Should().BeNull();
        Mission.Create(Draft(1000m, FeeSchedule.Monthly), Now).Value.FeePerInstalment.Should().Be(83.33m);
    }

    [Fact]
    public void Une_mission_incomplete_est_refusee_point_par_point()
    {
        Result<Mission> refused = Mission.Create(
            new Mission.Draft((MissionKind)9, MissionServices.None, new DateOnly(2026, 1, 1), 10.001m, default, -1m, " ", null, null), Now);

        refused.Errors.Select(e => e.Code).Should().BeEquivalentTo(
            MissionErrors.KindInvalidCode, MissionErrors.ServicesInvalidCode, MissionErrors.FeeInvalidCode, MissionErrors.ScheduleInvalidCode,
            MissionErrors.RateInvalidCode, MissionErrors.SignatoryRequiredCode);
        Mission.Create(Draft() with { Services = (MissionServices)128 }, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(MissionErrors.ServicesInvalidCode);
        Mission.Create(Draft() with { ClientSignatory = new string('a', 121) }, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(MissionErrors.NameTooLongCode);
        Mission.Create(Draft(Mission.MaxAnnualFee + 1m), Now).Errors.Should().ContainSingle().Which.Code.Should().Be(MissionErrors.FeeInvalidCode);
    }

    [Fact]
    public void La_mise_a_jour_garde_la_mission_si_la_saisie_est_refusee()
    {
        Mission mission = Mission.Create(Draft(), Now).Value;

        mission.Update(Draft(0m), Now.AddDays(1)).IsFailure.Should().BeTrue();
        mission.AnnualFeeExclTax.Should().Be(2400m);
        mission.Update(Draft(3600m) with { Manager = " Jeanne Expert ", SignedOn = new DateOnly(2026, 1, 15) }, Now.AddDays(1)).IsSuccess.Should().BeTrue();
        mission.Manager.Should().Be("Jeanne Expert");
        mission.SignedOn.Should().Be(new DateOnly(2026, 1, 15));
        mission.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-10-26", "2026-01-01", "2026-12-31")]
    [InlineData("2024-07-01", "2026-10-26", "2026-07-01", "2027-06-30")]
    [InlineData("2026-12-01", "2026-10-26", "2026-12-01", "2027-11-30")]
    [InlineData("2024-02-29", "2026-03-01", "2026-02-28", "2027-02-27")]
    public void L_annee_de_mission_part_de_la_date_anniversaire(string start, string day, string from, string to)
    {
        Mission mission = Mission.Create(Draft(start: DateOnly.Parse(start, CultureInfo.InvariantCulture)), Now).Value;

        mission.YearOf(DateOnly.Parse(day, CultureInfo.InvariantCulture)).Should().Be((DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Un_temps_passe_se_saisit_au_jour_le_jour_douze_heures_au_plus()
    {
        DateOnly today = new(2026, 10, 26);
        Guid user = Guid.CreateVersion7();

        TimeEntry entry = TimeEntry.Create(Guid.CreateVersion7(), today, user, 90, TimeTask.Vat, "  TVA  ", today, Now).Value;
        entry.Note.Should().Be("TVA");
        entry.UserId.Should().Be(user);
        TimeEntry.Create(Guid.CreateVersion7(), today, user, 30, TimeTask.Other, " ", today, Now).Value.Note.Should().BeNull();

        TimeEntry.Create(Guid.CreateVersion7(), today.AddDays(1), user, 0, default, new string('n', 201), today, Now).Errors.Select(e => e.Code)
            .Should().BeEquivalentTo(MissionErrors.DateInFutureCode, MissionErrors.DurationInvalidCode, MissionErrors.TaskInvalidCode, MissionErrors.NoteTooLongCode);
        TimeEntry.Create(Guid.CreateVersion7(), today, user, TimeEntry.MaxMinutes + 1, TimeTask.Vat, null, today, Now).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(MissionErrors.DurationInvalidCode);
        MissionErrors.TimeEntryNotYours.Kind.Should().Be(ErrorKind.Forbidden);
    }
}
