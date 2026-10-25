using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>ADR-0005 : la météo des paiements et le contrôle fiscal d'entraînement se lisent de la même façon par HTTP (WASM) et en direct (MAUI hors ligne).</summary>
public sealed class InsightsGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_meteo_des_paiements_meme_reponse_et_meme_refus(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        await using ServiceProvider unknown = _harness.Gateway(transport, Guid.CreateVersion7());

        OperationResult<PaymentWeatherResponse> weather = await services.GetRequiredService<IInsightsGateway>().GetPaymentWeatherAsync(default);
        OperationResult<PaymentWeatherResponse> refused = await unknown.GetRequiredService<IInsightsGateway>().GetPaymentWeatherAsync(default);

        weather.IsSuccess.Should().BeTrue(string.Join(", ", weather.Errors.Select(e => e.Code)));
        weather.Value!.Forecast.Should().HaveCount(PaymentWeatherResponse.ForecastWeeks);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_controle_fiscal_d_entrainement_meme_reponse_et_meme_refus(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();
        await GatewayHarness.SaveCompanyProfileAsync(services);
        DateOnly from = new(2026, 1, 1);
        DateOnly to = new(2026, 12, 31);

        OperationResult<PracticeAuditResponse> own = await insights.GetPracticeAuditAsync(from, to, default);
        OperationResult<PracticeAuditResponse> other = await insights.RunPracticeAuditAsync("autre.txt", "pas un FEC"u8.ToArray(), null, null, default);
        OperationResult<PracticeAuditResponse> refused = await insights.GetPracticeAuditAsync(to, from, default);

        own.IsSuccess.Should().BeTrue(string.Join(", ", own.Errors.Select(e => e.Code)));
        own.Value!.Readable.Should().BeTrue();
        other.Value!.Readable.Should().BeFalse();
        other.Value.FileName.Should().Be("autre.txt");
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Fec.PeriodInvalid);
    }

    public void Dispose() => _harness.Dispose();
}
