using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Onboarding;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Lettra.UI.Tests.Cabinet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Onboarding;

/// <summary>Tutoriel des premières fois (vague 5, lane F) : parcours, bulle, mémoire du « vu », relance.</summary>
[Collection("culture")]
public sealed class GuidedTourTests : BunitContext
{
    private readonly BunitJSModuleInterop _ui;
    private readonly BunitJSModuleInterop _tour;

    public GuidedTourTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ui = JSInterop.SetupModule(UiModule.Path);
        _tour = JSInterop.SetupModule(TourModule.Path);
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<MainLayout> Coque() => Render<MainLayout>(p => p.Add(l => l.Body, b =>
    {
        b.OpenComponent<Home>(0);
        b.CloseComponent();
    }));

    private void NavigateurReel() => _tour.Setup<bool>("canAutoStart").SetResult(true);

    // ---------- données des parcours ----------

    [Theory]
    [InlineData("", "home")]
    [InlineData("quotes", "quotes")]
    [InlineData("quotes/new?customer=42", "quote-editor")]
    [InlineData("quotes/0199f0aa-0000-7000-8000-000000000001", "quote-editor")]
    [InlineData("invoices", "invoices")]
    [InlineData("invoices/new", "invoice-editor")]
    [InlineData("customers", "customers")]
    [InlineData("catalog", "catalog")]
    [InlineData("exports/fec", "fec")]
    [InlineData("synchronisation", "sync")]
    [InlineData("alerts", "alerts")]
    public void Chaque_ecran_a_son_parcours(string chemin, string parcours) =>
        TourCatalog.ForPath(chemin)!.Id.Should().Be(parcours);

    [Fact]
    public void Un_ecran_sans_parcours_n_en_lance_aucun()
    {
        TourCatalog.ForPath("suppliers").Should().BeNull();
        TourCatalog.ForPath("settings/company").Should().BeNull();
        TourCatalog.Find(TourCatalog.AssistantTourId)!.Matches("").Should().BeFalse("l'assistant se lance à son ouverture, pas par une route");
    }

    [Fact]
    public void Les_parcours_couvrent_les_ecrans_du_plan_et_visent_des_ecrans_qui_existent()
    {
        HashSet<string> routes =
        [
            .. typeof(MainLayout).Assembly.GetTypes().SelectMany(t => t.GetCustomAttributes<RouteAttribute>()).Select(r => r.Template.Trim('/')),
        ];

        TourCatalog.All.Select(t => t.Id).Should().Contain(["quotes", "quote-editor", "invoices", "customers", "catalog", "fec", "sync", "alerts", "assistant"]);
        TourCatalog.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        foreach (TourDefinition tour in TourCatalog.All.Where(t => t.Route is not null))
        {
            string route = tour.Route!.TrimEnd('*').TrimEnd('/');
            routes.Should().Contain(r => r == route || r.StartsWith(route + "/", StringComparison.Ordinal), $"le parcours {tour.Id} vise un écran");
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Chaque_etape_a_son_titre_et_son_texte_traduits(string culture)
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo(culture));
        IStringLocalizer<SharedResource> l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        foreach (TourDefinition tour in TourCatalog.All)
        {
            tour.Steps.Should().NotBeEmpty();
            foreach (TourStep step in tour.Steps)
            {
                l[tour.TitleKey(step)].ResourceNotFound.Should().BeFalse(tour.TitleKey(step));
                l[tour.TextKey(step)].ResourceNotFound.Should().BeFalse(tour.TextKey(step));
            }
        }
    }

    [Fact]
    public void L_etat_vu_se_relit_tel_qu_il_a_ete_ecrit()
    {
        Dictionary<string, int> vus = GuidedTours.Parse("quotes:2;home:1;abîmé;x:y");

        vus.Should().BeEquivalentTo(new Dictionary<string, int> { ["home"] = 1, ["quotes"] = 2 });
        GuidedTours.Format(vus).Should().Be("home:1;quotes:2");
        GuidedTours.StorageKey("Local|Claire Martin").Should().StartWith("lettra.tours.").And.NotContain("Claire");
        GuidedTours.StorageKey("Local|Claire Martin").Should().NotBe(GuidedTours.StorageKey("Local|Paul Durand"));
    }

    // ---------- placement ----------

    [Fact]
    public void La_bulle_se_place_sous_l_element_sinon_au_dessus_sinon_au_centre()
    {
        TourPlacement dessous = TourPlacement.Compute(new TourTargetRect(100, 900, 200, 40, 1440, 900), 1440, 900);
        dessous.Mode.Should().Be(TourBubbleMode.Below);
        dessous.Top.Should().Be(152);
        dessous.Left.Should().Be(900);
        dessous.Spot!.Top.Should().Be(94, "la découpe déborde de 6 px");

        TourPlacement bord = TourPlacement.Compute(new TourTargetRect(100, 1400, 30, 30, 1440, 900), 1440, 900);
        bord.Left.Should().Be(1440 - 340 - 16, "la bulle reste dans l'écran");

        TourPlacement dessus = TourPlacement.Compute(new TourTargetRect(800, 100, 200, 40, 1440, 900), 1440, 900);
        dessus.Mode.Should().Be(TourBubbleMode.Above);
        dessus.Bottom.Should().Be(900 - 800 + 12);
        dessus.BubbleStyle().Should().Be("bottom: 112px; left: 100px; width: 340px");

        TourPlacement menu = TourPlacement.Compute(new TourTargetRect(72, 12, 223, 764, 1440, 900), 1440, 900);
        menu.Mode.Should().Be(TourBubbleMode.Beside, "un menu latéral haut garde sa bulle à côté");
        menu.BubbleStyle().Should().Be("top: 72px; left: 247px; width: 340px");
        TourPlacement.Compute(new TourTargetRect(0, 0, 1200, 880, 1440, 900), 1440, 900).Mode.Should().Be(TourBubbleMode.Center);
        TourPlacement.Compute(new TourTargetRect(0, 0, 0, 0, 1440, 900), 1440, 900).Should().Match<TourPlacement>(p => p.Mode == TourBubbleMode.Center && p.Spot == null);
    }

    [Fact]
    public void Sur_telephone_la_bulle_devient_une_feuille_qui_evite_l_element()
    {
        TourPlacement haut = TourPlacement.Compute(new TourTargetRect(80, 16, 200, 40, 375, 812), 375, 812);
        haut.Mode.Should().Be(TourBubbleMode.SheetBottom);
        haut.BubbleStyle().Should().BeNull("la feuille est posée par la feuille de style");
        haut.Spot.Should().NotBeNull();

        TourPlacement.Compute(new TourTargetRect(760, 0, 375, 52, 375, 812), 375, 812).Mode
            .Should().Be(TourBubbleMode.SheetTop, "les onglets du bas restent visibles");
    }

    // ---------- démarrage automatique ----------

    [Fact]
    public void Sans_navigateur_ou_sous_un_navigateur_pilote_rien_ne_demarre_seul()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.FindAll("#tour-bubble").Should().BeEmpty("bUnit, Playwright et les captures ne doivent jamais voir la bulle");
        coque.Find("#tour-help").GetAttribute("aria-label").Should().Be("Tutoriel de cet écran");
    }

    [Fact]
    public void La_premiere_visite_joue_le_parcours_de_l_ecran_et_le_retient()
    {
        NavigateurReel();
        IRenderedComponent<MainLayout> coque = Coque();

        coque.WaitForAssertion(() => coque.Find("#tour-bubble").GetAttribute("role").Should().Be("dialog"));
        IElement bulle = coque.Find("#tour-bubble");
        bulle.GetAttribute("aria-modal").Should().Be("true");
        coque.Find("#tour-title").TextContent.Should().Be("Bienvenue dans Lettra");
        coque.Find("#tour-count").TextContent.Should().Be("Étape 1 sur 7");
        coque.FindAll("#tour-previous").Should().BeEmpty();
        coque.WaitForAssertion(() => coque.Find("#tour-live").TextContent.Should().Be("Étape 1 sur 7. Bienvenue dans Lettra"));

        coque.Find("#tour-next").Click();
        coque.Find("#tour-title").TextContent.Should().Be("Les écrans");
        _tour.Invocations.Should().Contain(i => i.Identifier == "locate" && ((string[])i.Arguments[0]!).Contains(".lt-sidebar nav"));

        coque.Find("#tour-skip").Click();
        coque.FindAll("#tour-bubble").Should().BeEmpty();
        _ui.Invocations.Should().Contain(i => i.Identifier == "writePreference"
            && ((string)i.Arguments[0]!).StartsWith("lettra.tours.", StringComparison.Ordinal) && Equals(i.Arguments[1], "home:2"));
        _tour.Invocations.Should().Contain(i => i.Identifier == "restoreFocus");
    }

    [Fact]
    public async Task Un_parcours_deja_vu_ne_se_rejoue_pas_mais_une_nouvelle_version_si()
    {
        NavigateurReel();
        _ui.Setup<string?>("readPreference", i => ((string)i.Arguments[0]!).StartsWith("lettra.tours.", StringComparison.Ordinal)).SetResult("home:2");

        IRenderedComponent<MainLayout> coque = Coque();
        coque.FindAll("#tour-bubble").Should().BeEmpty();

        GuidedTours tours = Services.GetRequiredService<GuidedTours>();
        TourDefinition plusRecent = TourCatalog.Find("home")! with { Version = 3 };
        (await coque.InvokeAsync(() => tours.StartIfNewAsync(plusRecent))).Should().BeTrue();
        coque.WaitForAssertion(() => coque.FindAll("#tour-bubble").Should().ContainSingle());
    }

    [Fact]
    public void Le_clavier_parcourt_le_tutoriel_et_echap_le_ferme()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();
        IElement racine() => coque.Find(".lt-tour");

        racine().KeyDown("ArrowRight");
        racine().KeyDown("ArrowRight");
        coque.Find("#tour-count").TextContent.Should().Be("Étape 3 sur 7");
        racine().KeyDown("ArrowLeft");
        coque.Find("#tour-count").TextContent.Should().Be("Étape 2 sur 7");

        racine().KeyDown("Escape");
        coque.FindAll("#tour-bubble").Should().BeEmpty();
    }

    [Fact]
    public void La_derniere_etape_termine_le_parcours()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();
        for (int i = 0; i < 6; i++)
        {
            coque.Find("#tour-next").Click();
        }

        coque.FindAll("#tour-next").Should().BeEmpty();
        coque.Find("#tour-skip").TextContent.Should().Be("Fermer");
        coque.Find("#tour-done").Click();
        coque.FindAll("#tour-bubble").Should().BeEmpty();
    }

    [Fact]
    public void L_element_montre_est_entoure_et_la_bulle_ancree_dessous()
    {
        _tour.Setup<TourTargetRect?>("locate", _ => true).SetResult(new TourTargetRect(10, 300, 400, 36, 1440, 900));
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#tour-help").Click();

        coque.WaitForAssertion(() => coque.Find(".lt-tour-spot").GetAttribute("style").Should().Be("top: 4px; left: 294px; width: 412px; height: 48px"));
        coque.Find("#tour-bubble").ClassList.Should().Contain("is-below");
        coque.Find("#tour-bubble").GetAttribute("style").Should().Be("top: 58px; left: 300px; width: 340px");
    }

    [Fact]
    public void Reinitialiser_les_tutoriels_efface_l_etat_vu()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#appearance-toggle").Click();

        coque.Find(".lt-tour-reset").Click();

        coque.Find("#appearance-panel [role=status]").TextContent.Should().Be("Les tutoriels se rejoueront à la prochaine visite de chaque écran.");
        _ui.Invocations.Should().Contain(i => i.Identifier == "writePreference"
            && ((string)i.Arguments[0]!).StartsWith("lettra.tours.", StringComparison.Ordinal) && Equals(i.Arguments[1], string.Empty));
    }

    [Fact]
    public async Task La_palette_relance_le_tutoriel_de_l_ecran()
    {
        TourPaletteProvider palette = Services.GetServices<ICommandPaletteProvider>().OfType<TourPaletteProvider>().Single();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("quotes");

        IReadOnlyList<PaletteItem> items = await palette.SearchAsync("tutoriel", CancellationToken.None);

        items.Select(i => i.Label).Should().Equal("Tutoriel de cet écran", "Réinitialiser les tutoriels");
        await items[0].Run();
        Services.GetRequiredService<GuidedTours>().Active!.Id.Should().Be("quotes");
    }
}
