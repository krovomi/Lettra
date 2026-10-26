using System.Globalization;
using Bunit;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Insights;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Insights;

/// <summary>Écran « Remonter le temps » : l'instant choisi, la chaîne vérifiée, les chiffres d'alors et d'aujourd'hui.</summary>
[Collection("culture")]
public sealed class DossierAsOfPageTests : BunitContext
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private readonly FakeInsightsGateway _insights = new();

    public DossierAsOfPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInsightsGateway>(_insights);
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static DossierAsOfResponse View(DateTimeOffset at, bool intact = true, long replayed = 42) => new(
        at,
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Paris).DateTime),
        replayed,
        7,
        intact,
        new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
        new DossierAsOfTotalsResponse(12_000m, 2_400m, 14_400m),
        [.. Enumerable.Range(1, 9).Select(m => new DossierAsOfMonthResponse(m, m * 100m, m * 20m, m * 120m, m))],
        new DossierAsOfCountsResponse(3, 9, 1, 4, 8),
        1_800m,
        600m,
        [
            new(Guid.CreateVersion7(), "Atelier Dupont", 2_400m, 0m, 1_200m, 1_200m, 0m),
            new(Guid.CreateVersion7(), "Martin SARL", 600m, 0m, 0m, 600m, 600m),
        ],
        [
            new(Guid.CreateVersion7(), "FA-2026-0007", DossierAsOfDocumentKindDto.Invoice, new DateOnly(2026, 9, 2), Guid.CreateVersion7(), "Atelier Dupont", 1_200m, 1_200m, 0m),
            new(Guid.CreateVersion7(), "FA-2026-0009", DossierAsOfDocumentKindDto.DownPaymentInvoice, new DateOnly(2026, 9, 20), Guid.CreateVersion7(), "Martin SARL", 600m, 600m, 600m),
        ],
        new DossierAsOfClosingResponse("CLO-000008", new DateOnly(2026, 8, 31), new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero), 9_000m),
        [new(42, new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero), DossierAsOfEventKindDto.PaymentRecorded, "ENC-2026-0004", 1_200m)]);

    private IRenderedComponent<DossierAsOfPage> Open()
    {
        IRenderedComponent<DossierAsOfPage> page = Render<DossierAsOfPage>();
        page.WaitForState(() => page.FindAll("#asof-result").Count > 0);
        return page;
    }

    [Fact]
    public void L_ecran_s_ouvre_sur_la_fin_du_mois_dernier_et_montre_le_dossier_d_alors()
    {
        _insights.AsOf = View(new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(2)));

        IRenderedComponent<DossierAsOfPage> page = Open();

        _insights.Moments.Should().Equal([new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(2))]);
        page.Find("#asof-moment").TextContent.Should().Be("Le dossier le mercredi 30 septembre 2026 à 23:59");
        page.Find("#asof-chain").ClassList.Should().Contain("is-success");
        page.Find("#asof-chain").TextContent.Should().Contain("42 entrées rejouées");
        page.Find("#asof-since").TextContent.Should().Contain("7 événements");
        page.Find("#asof-revenue").TextContent.Should().Contain("000,00");
        page.Find("#asof-receivables-now").TextContent.Should().Contain("600,00");
        page.Find("#asof-counts").TextContent.Should().Be("1 avoirs · 3 devis · 4 encaissements");
        page.FindAll("#asof-months li").Should().HaveCount(9);
        page.Find("#asof-months li.is-current").TextContent.Should().Contain("septembre");
        page.FindAll("#asof-documents li").Should().HaveCount(2);
        page.Find("#asof-documents").TextContent.Should().Contain("acompte").And.Contain("soldée");
        page.Find("#asof-closing").TextContent.Should().Contain("CLO-000008").And.Contain("31/08/2026");
        page.Find("#asof-events").TextContent.Should().Contain("Encaissement enregistré ENC-2026-0004");
    }

    [Fact]
    public void Les_instants_proposes_et_l_heure_saisie_donnent_l_instant_dans_le_fuseau_du_poste()
    {
        _insights.AsOf = View(new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.FromHours(1)));
        IRenderedComponent<DossierAsOfPage> page = Open();

        page.Find("#asof-preset-year").Click();
        page.Find("#asof-preset-week").Click();
        page.Find("#asof-date").Change("29/03/2026");
        page.Find("#asof-time").Change("2h30");
        page.Find("#asof-go").Click();

        _insights.Moments.Skip(1).Should().Equal(
        [
            new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)),
        ]);
    }

    [Fact]
    public void Une_heure_illisible_est_signalee_sans_appeler_le_serveur()
    {
        _insights.AsOf = View(new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(2)));
        IRenderedComponent<DossierAsOfPage> page = Open();

        page.Find("#asof-time").Change("25:99");
        page.Find("#asof-go").Click();

        page.Find("#asof-time-error").TextContent.Should().Contain("18:00");
        _insights.Moments.Should().ContainSingle();
    }

    [Fact]
    public void Une_chaine_rompue_et_un_dossier_encore_vide_sont_dits()
    {
        _insights.AsOf = View(new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(2)), intact: false);
        IRenderedComponent<DossierAsOfPage> page = Open();
        page.Find("#asof-chain").ClassList.Should().Contain("is-danger");

        _insights.AsOf = View(new DateTimeOffset(2025, 12, 31, 23, 59, 0, TimeSpan.FromHours(1)), replayed: 0) with { OpenDocuments = [], Customers = [], LastEvents = [], LastClosing = null };
        page.Find("#asof-preset-year").Click();

        page.Find("#asof-empty").TextContent.Should().Contain("05/01/2026");
        page.FindAll("#asof-chain").Should().BeEmpty();
        page.Find("#asof-documents-empty").Should().NotBeNull();
        page.Find("#asof-customers-empty").Should().NotBeNull();
    }

    [Fact]
    public void Les_listes_se_filtrent_et_se_trient()
    {
        _insights.AsOf = View(new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.FromHours(2)));
        IRenderedComponent<DossierAsOfPage> page = Open();

        page.Find("#asof-documents-search").Input("martin");
        page.FindAll("#asof-documents li").Should().ContainSingle().Which.TextContent.Should().Contain("FA-2026-0009");
        page.Find("#asof-documents-count").TextContent.Should().Be("1 sur 2");

        page.Find("#asof-customers-sort").Change("Customer:Descending");
        page.FindAll("#asof-customers li")[0].TextContent.Should().Contain("Martin SARL");
        page.Find("#asof-customers-search").Input("dupont");
        page.FindAll("#asof-customers li").Should().ContainSingle().Which.TextContent.Should().Contain("Atelier Dupont");
    }

    [Theory]
    [InlineData("18:00", 18, 0)]
    [InlineData("9:05", 9, 5)]
    [InlineData("18h", 18, 0)]
    [InlineData("18H30", 18, 30)]
    [InlineData(" 7 ", 7, 0)]
    [InlineData("", 23, 59)]
    public void L_heure_se_lit_sous_plusieurs_formes(string text, int hour, int minute)
    {
        AsOfTime.TryParse(text, out TimeOnly time).Should().BeTrue();
        time.Should().Be(new TimeOnly(hour, minute));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("midi")]
    [InlineData("18:5x")]
    public void Une_heure_illisible_est_refusee(string text) => AsOfTime.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void L_heure_vecue_deux_fois_a_l_automne_prend_la_premiere() =>
        AsOfTime.Instant(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Paris).Should().Be(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)));

    private sealed class ParisClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => Paris;
    }
}
