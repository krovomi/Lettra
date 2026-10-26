using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>ADR-0005 : la météo des paiements, le contrôle fiscal d'entraînement, la preuve d'authenticité, le dossier d'un instant passé, « Pourquoi ce chiffre ? », le simulateur « et si ? » et la fiche d'appel se lisent de la même façon par HTTP (WASM) et en direct (MAUI hors ligne).</summary>
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

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task La_preuve_d_authenticite_meme_verdict_par_les_deux_transports(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();

        OperationResult<InvoiceProofCheckResponse> unreadable = await insights.VerifyInvoiceProofAsync("pas une preuve", default);
        OperationResult<IReadOnlyList<InvoiceProofKeyResponse>> keys = await insights.ListInvoiceProofKeysAsync(default);
        OperationResult<InvoiceProofResponse> missing = await insights.GetInvoiceProofAsync(Guid.CreateVersion7(), default);

        unreadable.Value!.Verdict.Should().Be(InvoiceProofVerdictDto.Unreadable);
        keys.IsSuccess.Should().BeTrue(string.Join(", ", keys.Errors.Select(e => e.Code)));
        missing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Invoice.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_dossier_d_un_instant_passe_meme_reponse_par_les_deux_transports(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();

        OperationResult<DossierAsOfResponse> past = await insights.GetDossierAsOfAsync(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)), default);

        past.IsSuccess.Should().BeTrue(string.Join(", ", past.Errors.Select(e => e.Code)));
        past.Value!.EntriesReplayed.Should().Be(0);
        past.Value.ChainIntact.Should().BeTrue();
        past.Value.LegalDate.Should().Be(new DateOnly(2000, 1, 1));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Fiche_d_appel_meme_refus_par_les_deux_transports_pour_un_client_inconnu(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();

        OperationResult<CallSheetResponse> unknown = await insights.GetCallSheetAsync(Guid.CreateVersion7(), default);

        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Customer.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Pourquoi_ce_chiffre_meme_explication_par_les_deux_transports(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();

        OperationResult<FigureExplanationResponse> past = await insights.ExplainFigureAsync(ExplainedFigureDto.MonthRevenue, new DateOnly(2001, 2, 10), default);
        OperationResult<FigureExplanationResponse> overdue = await insights.ExplainFigureAsync(ExplainedFigureDto.Overdue, null, default);

        past.IsSuccess.Should().BeTrue(string.Join(", ", past.Errors.Select(e => e.Code)));
        past.Value!.Month.Should().Be(new DateOnly(2001, 2, 1));
        past.Value.Lines.Should().BeEmpty();
        overdue.Value!.Figure.Should().Be(ExplainedFigureDto.Overdue);
        overdue.Value.Month.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_simulateur_meme_reponse_et_meme_refus_par_les_deux_transports(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IInsightsGateway insights = services.GetRequiredService<IInsightsGateway>();

        OperationResult<WhatIfResponse> simulated = await insights.SimulateWhatIfAsync(new WhatIfRequest(ExtraDelayDays: 15), default);
        OperationResult<WhatIfResponse> refused = await insights.SimulateWhatIfAsync(new WhatIfRequest(ExtraDelayDays: 999), default);

        simulated.IsSuccess.Should().BeTrue(string.Join(", ", simulated.Errors.Select(e => e.Code)));
        simulated.Value!.Weeks.Should().HaveCount(WhatIfRequest.Weeks);
        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Invalid);
    }

    public void Dispose() => _harness.Dispose();
}
