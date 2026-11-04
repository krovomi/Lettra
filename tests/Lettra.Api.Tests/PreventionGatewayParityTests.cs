using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>ADR-0005 : le radar de prévention se lit, se règle et prépare son courrier de la même façon par HTTP (WASM) et en direct (MAUI hors ligne).</summary>
public sealed class PreventionGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_radar_et_sa_carte_meme_reponse_et_meme_refus(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        await using ServiceProvider unknown = _harness.Gateway(transport, Guid.CreateVersion7());
        IPreventionGateway prevention = services.GetRequiredService<IPreventionGateway>();

        OperationResult<PreventionRadarResponse> radar = await prevention.GetRadarAsync(default);
        OperationResult<PreventionSummaryResponse> summary = await prevention.GetSummaryAsync(default);
        OperationResult<PreventionRadarResponse> refused = await unknown.GetRequiredService<IPreventionGateway>().GetRadarAsync(default);

        radar.IsSuccess.Should().BeTrue(GatewayHarness.Codes(radar));
        radar.Value!.Signals.Should().HaveCount(Enum.GetValues<PreventionSignalDto>().Length);
        radar.Value.CanAdjust.Should().BeTrue();
        summary.Value!.Visible.Should().BeTrue();
        summary.Value.ActiveSignals.Should().Be(radar.Value.ActiveSignals);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Les_seuils_et_le_courrier_meme_reponse_et_meme_refus(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IPreventionGateway prevention = services.GetRequiredService<IPreventionGateway>();
        PreventionRadarResponse radar = (await prevention.GetRadarAsync(default)).Value!;
        UpdatePreventionSettingsRequest request = UpdatePreventionSettingsRequest.From(radar.Settings);
        request.PeerGapPoints = 25;

        OperationResult<PreventionSettingsResponse> saved = await prevention.UpdateSettingsAsync(request, default);
        OperationResult<PreventionSettingsResponse> stale = await prevention.UpdateSettingsAsync(request, default);
        OperationResult<PreventionLetterResponse> letter = await prevention.GetLetterAsync("en-US", default);
        OperationResult<bool> invalid = await prevention.SendLetterAsync(new SendPreventionLetterRequest { To = "", Subject = "x", Body = "y" }, default);

        saved.IsSuccess.Should().BeTrue(GatewayHarness.Codes(saved));
        saved.Value!.PeerGapPoints.Should().Be(25);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        letter.Value!.Language.Should().Be("en-US");
        invalid.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Prevention.RecipientRequired, nameof(SendPreventionLetterRequest.To)));
    }

    public void Dispose() => _harness.Dispose();
}
