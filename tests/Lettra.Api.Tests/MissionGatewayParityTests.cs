using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Lettre de mission et temps passé par la même interface, en HTTP et en direct : mêmes résultats, mêmes refus.</summary>
public sealed class MissionGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static SaveMissionRequest Mission(decimal fee = 2400m) => new()
    {
        Kind = MissionKindDto.Presentation,
        Services = [MissionServiceDto.Bookkeeping, MissionServiceDto.AnnualAccounts],
        StartDate = new DateOnly(Today.Year, 1, 1),
        AnnualFeeExclTax = fee,
        Schedule = FeeScheduleDto.Monthly,
        TargetHourlyRate = 80m,
        ClientSignatory = "Claire Martin",
        Manager = "Jeanne Expert",
    };

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_mission_s_enregistre_et_le_temps_passe_donne_le_taux_horaire(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IMissionGateway missions = services.GetRequiredService<IMissionGateway>();
        long? version = (await missions.GetAsync(default)).Value!.Mission?.Version;

        OperationResult<MissionOverviewResponse> saved = await missions.SaveAsync(Mission(), version, default);
        OperationResult<MissionOverviewResponse> stale = await missions.SaveAsync(Mission(3000m), saved.Value!.Mission!.Version - 1, default);
        OperationResult<MissionOverviewResponse> added = await missions.AddTimeAsync(
            new AddTimeEntryRequest { Date = Today, Minutes = 90, Task = TimeTaskDto.Vat, Note = "  TVA du mois  " }, default);
        TimeEntryResponse entry = added.Value!.Entries.First(e => e.Note == "TVA du mois");
        OperationResult<MissionOverviewResponse> deleted = await missions.DeleteTimeAsync(entry.Id, default);

        saved.IsSuccess.Should().BeTrue(GatewayHarness.Codes(saved));
        MissionResponse mission = saved.Value.Mission;
        mission.Services.Should().Equal(MissionServiceDto.Bookkeeping, MissionServiceDto.AnnualAccounts);
        mission.FeePerInstalment.Should().Be(200m);
        mission.BudgetHours.Should().Be(30m);
        saved.Value.YearStart.Should().Be(new DateOnly(Today.Year, 1, 1));
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        added.IsSuccess.Should().BeTrue(GatewayHarness.Codes(added));
        entry.Mine.Should().BeTrue();
        entry.Minutes.Should().Be(90);
        added.Value.MinutesSpent.Should().BeGreaterThanOrEqualTo(90);
        added.Value.EarnedExclTax.Should().BeGreaterThan(0m);
        added.Value.EffectiveHourlyRate.Should().NotBeNull();
        added.Value.ByTask.Should().Contain(t => t.Task == TimeTaskDto.Vat);
        deleted.Value!.Entries.Should().NotContain(e => e.Id == entry.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IMissionGateway missions = services.GetRequiredService<IMissionGateway>();
        long? version = (await missions.GetAsync(default)).Value!.Mission?.Version;

        OperationResult<MissionOverviewResponse> noService = await missions.SaveAsync(Mission() with { Services = [] }, version, default);
        OperationResult<MissionOverviewResponse> noFee = await missions.SaveAsync(Mission(0m), version, default);
        OperationResult<MissionOverviewResponse> noSignatory = await missions.SaveAsync(Mission() with { ClientSignatory = " " }, version, default);
        OperationResult<MissionOverviewResponse> future = await missions.AddTimeAsync(new AddTimeEntryRequest { Date = Today.AddDays(1), Minutes = 30 }, default);
        OperationResult<MissionOverviewResponse> tooLong = await missions.AddTimeAsync(new AddTimeEntryRequest { Date = Today, Minutes = 13 * 60 }, default);
        OperationResult<MissionOverviewResponse> unknown = await missions.DeleteTimeAsync(Guid.CreateVersion7(), default);

        noService.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Mission.ServicesInvalid);
        noFee.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Mission.FeeInvalid);
        noSignatory.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Mission.SignatoryRequired);
        future.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Mission.DateInFuture);
        tooLong.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Mission.DurationInvalid);
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Mission.TimeEntryNotFound);
    }

    [Fact]
    public async Task Un_collaborateur_saisit_son_temps_mais_ne_change_pas_les_honoraires_ni_le_temps_d_un_autre()
    {
        await using ServiceProvider manager = _harness.HttpGateway(LettraApiFactory.DossierId);
        await using ServiceProvider collaborator = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.CollaborateurId);
        IMissionGateway managerMissions = manager.GetRequiredService<IMissionGateway>();
        IMissionGateway collaboratorMissions = collaborator.GetRequiredService<IMissionGateway>();
        Guid managerEntry = (await managerMissions.AddTimeAsync(new AddTimeEntryRequest { Date = Today, Minutes = 15, Note = "manager" }, default))
            .Value!.Entries.First(e => e.Note == "manager").Id;

        OperationResult<MissionOverviewResponse> fees = await collaboratorMissions.SaveAsync(Mission(), null, default);
        OperationResult<MissionOverviewResponse> own = await collaboratorMissions.AddTimeAsync(new AddTimeEntryRequest { Date = Today, Minutes = 45, Note = "collaborateur" }, default);
        OperationResult<MissionOverviewResponse> others = await collaboratorMissions.DeleteTimeAsync(managerEntry, default);

        fees.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        own.IsSuccess.Should().BeTrue(GatewayHarness.Codes(own));
        own.Value!.Entries.Single(e => e.Id == managerEntry).Mine.Should().BeFalse();
        others.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Mission.TimeEntryNotYours);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IMissionGateway missions = services.GetRequiredService<IMissionGateway>();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await missions.GetAsync(default)).Errors,
            (await missions.SaveAsync(Mission(), 1, default)).Errors,
            (await missions.AddTimeAsync(new AddTimeEntryRequest { Date = Today, Minutes = 30 }, default)).Errors,
            (await missions.DeleteTimeAsync(Guid.CreateVersion7(), default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
