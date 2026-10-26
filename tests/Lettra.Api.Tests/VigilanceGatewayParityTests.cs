using Lettra.Contracts.Common;
using Lettra.Contracts.Missions;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Fiche anti-blanchiment par la même interface, en HTTP et en direct : mêmes résultats, mêmes refus, même blocage de la lettre de mission.</summary>
public sealed class VigilanceGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    private static SaveDueDiligenceRequest File(RiskLevelDto level = RiskLevelDto.Standard) => new()
    {
        Evidence = [IdentityEvidenceDto.CompanyRegistration, IdentityEvidenceDto.ManagerIdentity],
        IdentityVerifiedOn = Today,
        Owners = [new BeneficialOwnerDto { Name = "Claire Martin", SharePercent = 70m, Kind = OwnershipKindDto.Capital }],
        Level = level,
        ReviewedOn = Today,
    };

    private static SaveMissionRequest Mission(DateOnly? signedOn) => new()
    {
        Kind = MissionKindDto.Presentation,
        Services = [MissionServiceDto.Bookkeeping],
        StartDate = new DateOnly(Today.Year, 1, 1),
        AnnualFeeExclTax = 2400m,
        ClientSignatory = "Claire Martin",
        SignedOn = signedOn,
    };

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_lettre_de_mission_ne_se_signe_qu_avec_une_fiche_a_jour(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IVigilanceGateway vigilance = services.GetRequiredService<IVigilanceGateway>();
        IMissionGateway missions = services.GetRequiredService<IMissionGateway>();
        OperationResult<VigilanceOverviewResponse> before = await vigilance.GetAsync(default);
        OperationResult<MissionOverviewResponse> unsigned = await missions.SaveAsync(Mission(null), (await missions.GetAsync(default)).Value!.Mission?.Version, default);

        OperationResult<MissionOverviewResponse> refused = await missions.SaveAsync(Mission(Today), unsigned.Value!.Mission!.Version, default);
        OperationResult<VigilanceOverviewResponse> saved = await vigilance.SaveAsync(File(), before.Value!.File?.Version, default);
        OperationResult<VigilanceOverviewResponse> stale = await vigilance.SaveAsync(File(), saved.Value!.File!.Version - 1, default);
        OperationResult<MissionOverviewResponse> signed = await missions.SaveAsync(Mission(Today), unsigned.Value.Mission.Version, default);

        before.Value.Status.Should().Be(DueDiligenceStatusDto.Missing);
        unsigned.Value.ClientKnown.Should().BeFalse();
        refused.Errors.Should().ContainSingle().Which.Should().Match<ErrorDetail>(e => e.Code == ErrorCodes.Mission.DueDiligenceRequired && e.Target == "SignedOn");
        saved.IsSuccess.Should().BeTrue(GatewayHarness.Codes(saved));
        saved.Value.Status.Should().Be(DueDiligenceStatusDto.UpToDate);
        saved.Value.File.NextReviewOn.Should().Be(Today.AddYears(2));
        saved.Value.File.Owners.Should().ContainSingle().Which.SharePercent.Should().Be(70m);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        signed.IsSuccess.Should().BeTrue(GatewayHarness.Codes(signed));
        signed.Value!.Mission!.SignedOn.Should().Be(Today);
        signed.Value.ClientKnown.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_refus_sont_les_memes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IVigilanceGateway vigilance = services.GetRequiredService<IVigilanceGateway>();
        long? version = (await vigilance.GetAsync(default)).Value!.File?.Version;
        SaveDueDiligenceRequest exposed = File() with { Factors = [RiskFactorDto.PoliticallyExposed] };
        SaveDueDiligenceRequest lowShare = File() with { Owners = [new BeneficialOwnerDto { Name = "Paul Martin", SharePercent = 20m, Kind = OwnershipKindDto.Capital }] };

        OperationResult<VigilanceOverviewResponse> tooLow = await vigilance.SaveAsync(exposed, version, default);
        OperationResult<VigilanceOverviewResponse> unexplained = await vigilance.SaveAsync(File(RiskLevelDto.High), version, default);
        OperationResult<VigilanceOverviewResponse> share = await vigilance.SaveAsync(lowShare, version, default);
        OperationResult<VigilanceOverviewResponse> future = await vigilance.SaveAsync(File() with { ReviewedOn = Today.AddDays(1) }, version, default);

        tooLow.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Vigilance.LevelTooLow);
        unexplained.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Vigilance.JustificationRequired);
        share.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Vigilance.OwnerShareInvalid);
        future.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Vigilance.DateInFuture);
    }

    [Fact]
    public async Task Un_collaborateur_lit_la_fiche_sans_la_changer_et_le_client_ne_la_voit_pas()
    {
        await using ServiceProvider collaborator = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.CollaborateurId);
        await using ServiceProvider portal = _harness.HttpGateway(LettraApiFactory.DossierId, LettraApiFactory.PortailId);
        IVigilanceGateway collaboratorFile = collaborator.GetRequiredService<IVigilanceGateway>();

        OperationResult<VigilanceOverviewResponse> read = await collaboratorFile.GetAsync(default);
        OperationResult<VigilanceOverviewResponse> write = await collaboratorFile.SaveAsync(File(), read.Value?.File?.Version, default);
        OperationResult<VigilanceOverviewResponse> client = await portal.GetRequiredService<IVigilanceGateway>().GetAsync(default);

        read.IsSuccess.Should().BeTrue(GatewayHarness.Codes(read));
        write.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
        client.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IVigilanceGateway vigilance = services.GetRequiredService<IVigilanceGateway>();

        (await vigilance.GetAsync(default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
        (await vigilance.SaveAsync(File(), 1, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable);
    }

    public void Dispose() => _harness.Dispose();
}
