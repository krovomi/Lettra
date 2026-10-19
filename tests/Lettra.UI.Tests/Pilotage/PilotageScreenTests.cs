using System.Globalization;
using Bunit;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Pilotage;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Pilotage;
using Lettra.UI.Tests.Cabinet;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Pilotage;

/// <summary>
/// Tableaux de bord dans Lettra : le dossier ouvert, « Mes dossiers » triables et filtrables, et l'onglet
/// « Administration » qui n'apparaît qu'à qui administre un dossier. L'écran n'affiche que ce que le serveur renvoie.
/// </summary>
[Collection("culture")]
public sealed class PilotageScreenTests : BunitContext
{
    private readonly FakePilotageGateway _gateway = new();
    private readonly SwitchableDossierSelection _selection = new(FakeCabinetGateway.Boulangerie);

    public PilotageScreenTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IDossierSelection>(_selection);
        Services.AddSingleton<IPilotageGateway>(_gateway);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Le_dossier_ouvert_montre_ses_indicateurs_colores_ses_douze_mois_et_ce_qui_reste_a_faire()
    {
        IRenderedComponent<PilotagePage> page = Render<PilotagePage>();

        page.WaitForAssertion(() => Plain(page.Find("#pilotage-dso .lt-kpi-value").TextContent).Should().Be("62 j"));
        page.Find("#pilotage-dso").GetAttribute("data-level").Should().Be("Critical");
        Plain(page.Find("#pilotage-trend .lt-kpi-value").TextContent).Should().Be("−12 %");
        page.Find("#pilotage-trend").GetAttribute("data-level").Should().Be("Warning");
        page.FindAll("#pilotage-chart-invoices .lt-bar").Should().HaveCount(12);
        page.Find("#pilotage-todo a").GetAttribute("href").Should().Be("invoices");
        page.Find("#pilotage-todo").TextContent.Should().Contain("3");
        page.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).Should().Equal("Ce dossier", "Mes dossiers", "Administration");
    }

    [Fact]
    public void Mes_dossiers_se_filtre_se_trie_et_ouvre_un_dossier_en_le_disant_dans_le_toaster()
    {
        IRenderedComponent<PilotagePage> page = Render<PilotagePage>();
        page.WaitForAssertion(() => page.FindAll("[role=tab]").Should().HaveCount(3));

        page.Find("#pilotage-tab-dossiers").Click();
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("?vue=dossiers");
        Names(page).Should().Equal("Atelier Éole", "Boulangerie Martin", "Garage du Centre");
        page.Find("#pilotage-dossiers-count .lt-kpi-value").TextContent.Should().Be("3");

        page.Find("#pilotage-sort-dso").Click();
        Names(page)[0].Should().Be("Garage du Centre", "le délai le plus long en premier");

        page.Find("#pilotage-search").Input("eole");
        Names(page).Should().Equal("Atelier Éole");
        page.Find("#pilotage-search").Input("zzz");
        page.Find("#pilotage-dossiers-filtered").Should().NotBeNull();
        page.Find("#pilotage-search").Input(string.Empty);

        page.FindAll("#pilotage-dossiers tbody tr").Single(r => r.TextContent.Contains("Garage", StringComparison.Ordinal)).QuerySelector("button")!.Click();

        page.WaitForAssertion(() => _selection.CurrentDossierId.Should().Be(FakeCabinetGateway.Garage));
        this.ToastText().Should().Be("Dossier « Garage du Centre » ouvert.");
        page.WaitForAssertion(() => page.Find("#pilotage-tab-dossier").GetAttribute("aria-selected").Should().Be("true"));
        _gateway.DossierReads.Should().Be(2, "le tableau du dossier choisi est relu");
    }

    [Fact]
    public void L_administration_n_apparait_qu_a_qui_administre_un_dossier()
    {
        _gateway.Administration = new AdministrationPilotageResponse(true, DateTimeOffset.UtcNow, []);
        IRenderedComponent<PilotagePage> page = RenderAt("pilotage?vue=administration");

        page.WaitForAssertion(() => page.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).Should().Equal("Ce dossier", "Mes dossiers"));
        page.Find("#pilotage-tab-dossier").GetAttribute("aria-selected").Should().Be("true", "l'onglet demandé n'existe pas pour lui");
    }

    [Fact]
    public void L_administration_montre_la_sante_de_chaque_dossier_et_se_trie()
    {
        IRenderedComponent<PilotagePage> page = RenderAt("pilotage?vue=administration");

        page.WaitForAssertion(() => page.FindAll("#pilotage-admin tbody tr").Should().HaveCount(2));
        Plain(page.Find("#pilotage-admin-size .lt-kpi-value").TextContent).Should().Be("2,5 Mio");
        page.Find("#pilotage-admin-silent").GetAttribute("data-level").Should().Be("Warning");
        page.Find("#pilotage-admin-nightly").GetAttribute("data-level").Should().Be("Critical");

        page.Find("#pilotage-admin-sort-size").Click();
        page.Find("#pilotage-admin tbody th").TextContent.Should().Be("Garage du Centre");
    }

    [Fact]
    public void Sans_collecte_sur_ce_poste_mes_dossiers_le_dit_et_un_dossier_illisible_aussi()
    {
        _gateway.Cabinet = new CabinetPilotageResponse(false, null, []);
        _gateway.Administration = new AdministrationPilotageResponse(false, null, []);
        _gateway.DossierError = ErrorCodes.Api.Unreachable;
        IRenderedComponent<PilotagePage> page = Render<PilotagePage>();

        page.WaitForAssertion(() => page.Find("#pilotage-dossier-unavailable").Should().NotBeNull());
        page.Find("#pilotage-tab-dossiers").Click();
        page.Find("#pilotage-cabinet-unavailable").TextContent.Should().Contain("serveur du cabinet");

        page.Find("#pilotage-refresh").Click();
        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle().Which.Kind.Should().Be(Lettra.UI.Shared.Services.ToastKind.Error));
    }

    [Fact]
    public void Actualiser_relit_les_trois_vues_et_le_dit_dans_le_toaster()
    {
        IRenderedComponent<PilotagePage> page = Render<PilotagePage>();
        page.WaitForAssertion(() => page.Find("#pilotage-dso").Should().NotBeNull());

        page.Find("#pilotage-refresh").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Tableaux de bord actualisés."));
        _gateway.DossierReads.Should().Be(2);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(512L, "512 o")]
    [InlineData(1_572_864L, "1,5 Mio")]
    public void Les_tailles_s_ecrivent_en_unites_binaires(long? bytes, string expected) =>
        Plain(PilotageDisplay.Bytes(bytes, CultureInfo.GetCultureInfo("fr-FR"))).Should().Be(expected);

    [Theory]
    [InlineData(null, "—")]
    [InlineData(30d, "30 s")]
    [InlineData(600d, "10 min")]
    [InlineData(7200d, "2 h")]
    [InlineData(259_200d, "3 j")]
    public void Les_durees_s_ecrivent_dans_l_unite_lisible(double? seconds, string expected) =>
        Plain(PilotageDisplay.Duration(seconds, CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void Les_seuils_suivent_ceux_des_tableaux_grafana()
    {
        PilotageDisplay.Dso(44).Should().Be(IndicatorLevelDto.Ok);
        PilotageDisplay.Dso(45).Should().Be(IndicatorLevelDto.Warning);
        PilotageDisplay.Overdue(0.4).Should().Be(IndicatorLevelDto.Critical);
        PilotageDisplay.Collection(0.7).Should().Be(IndicatorLevelDto.Warning);
        PilotageDisplay.Collection(0.5).Should().Be(IndicatorLevelDto.Critical);
        PilotageDisplay.Collection(null).Should().Be(IndicatorLevelDto.Ok);
        PilotageDisplay.Concentration(0.55).Should().Be(IndicatorLevelDto.Critical);
        PilotageDisplay.Trend(-0.3).Should().Be(IndicatorLevelDto.Critical);
        PilotageDisplay.Trend(null).Should().Be(IndicatorLevelDto.Ok);
        PilotageDisplay.NightlyAge(27 * 3600).Should().Be(IndicatorLevelDto.Warning);
        PilotageDisplay.Silent(0).Should().Be(IndicatorLevelDto.Ok);
        Plain(PilotageDisplay.SignedPercent(0.12, CultureInfo.InvariantCulture)).Should().Be("+12 %");
        Plain(PilotageDisplay.SignedPercent(0.001, CultureInfo.InvariantCulture)).Should().Be("0 %");
        PilotageDisplay.SignedPercent(null, CultureInfo.InvariantCulture).Should().Be("—");
        PilotageDisplay.Percent(null, CultureInfo.InvariantCulture).Should().Be("—");
        PilotageDisplay.Days(null, CultureInfo.InvariantCulture).Should().Be("—");
        PilotageDisplay.BarHeight(5, 0).Should().Be("0%");
        PilotageDisplay.BarHeight(1, 1000).Should().Be("2%", "une valeur non nulle garde un trait");
        PilotageDisplay.BarHeight(50, 100).Should().Be("50%");
        PilotageDisplay.Month("2026-10", true, CultureInfo.GetCultureInfo("fr-FR")).Should().Be("octobre 2026");
        PilotageDisplay.Month("inconnu", false, CultureInfo.InvariantCulture).Should().Be("inconnu");
    }

    /// <summary>Les espaces insécables (avant une unité, dans un pourcentage) lus comme des espaces.</summary>
    private static string Plain(string text) => text.Replace('\u00A0', ' ').Replace('\u202F', ' ').Trim();

    private IRenderedComponent<PilotagePage> RenderAt(string uri)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(uri);
        return Render<PilotagePage>();
    }

    private static List<string> Names(IRenderedComponent<PilotagePage> page) =>
        [.. page.FindAll("#pilotage-dossiers tbody th").Select(t => t.TextContent.Trim())];

    private sealed class FakePilotageGateway : IPilotageGateway
    {
        private static readonly DateTimeOffset At = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

        public int DossierReads { get; private set; }

        public string? DossierError { get; set; }

        public CabinetPilotageResponse Cabinet { get; set; } = new(true, At,
        [
            Row(FakeCabinetGateway.Atelier, "Atelier Éole", 20),
            Row(FakeCabinetGateway.Boulangerie, "Boulangerie Martin", 35),
            Row(FakeCabinetGateway.Garage, "Garage du Centre", 70),
        ]);

        public AdministrationPilotageResponse? Administration { get; set; } = new(true, At,
        [
            new(FakeCabinetGateway.Atelier, "Atelier Éole", 1_048_576, 2, 0, 0, 3600, 3600),
            new(FakeCabinetGateway.Garage, "Garage du Centre", 1_572_864, 1, 1, 1, 900_000, 200_000),
        ]);

        public Task<OperationResult<DossierPilotageResponse>> GetDossierAsync(CancellationToken cancellationToken)
        {
            DossierReads++;
            if (DossierError is { } code)
            {
                return Task.FromResult(OperationResult<DossierPilotageResponse>.Failure(code));
            }

            MonthlyActivityDto[] months = [.. Enumerable.Range(0, 12).Select(i => new MonthlyActivityDto(new DateOnly(2025, 11, 1).AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture), i == 11 ? 130 : 95, i))];
            DossierPilotageDto dossier = Row(FakeCabinetGateway.Boulangerie, "Boulangerie Martin", 62) with { RevenueTrendRatio = -0.12, Months = months };
            return Task.FromResult(OperationResult<DossierPilotageResponse>.Success(new(At, dossier, At,
                [new DossierIndicatorDto(DossierIndicatorKeys.OverdueInvoices, 3, IndicatorLevelDto.Warning, null)])));
        }

        public Task<OperationResult<CabinetPilotageResponse>> GetCabinetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<CabinetPilotageResponse>.Success(Cabinet));

        public Task<OperationResult<AdministrationPilotageResponse>> GetAdministrationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Administration is { } a
                ? OperationResult<AdministrationPilotageResponse>.Success(a)
                : OperationResult<AdministrationPilotageResponse>.Failure(ErrorCodes.Security.Forbidden));

        private static DossierPilotageDto Row(Guid id, string name, double dso) =>
            new(id, name, dso, 0.1, 0.9, 0.05, 4, 0.2, 0.5, 1, 0, []);
    }
}
