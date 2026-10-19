using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Design;

/// <summary>
/// Menu réglable : pas de barre horizontale, sections repliables par chevron, favoris épinglés, recherche du menu
/// (sans accents, par mots-clés, Entrée, Échap) et relais vers la palette ou l'aide quand le menu ne suffit pas.
/// </summary>
[Collection("culture")]
public sealed class NavMenuTests : BunitContext
{
    private const string Utilisateur = "Local|Claire Martin";

    private readonly BunitJSModuleInterop _module;

    public NavMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(UiModule.Path);
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

    private static IElement Section(IRenderedComponent<MainLayout> coque, string groupe) =>
        coque.Find($".lt-sidebar .lt-nav-section[data-group='{groupe}']");

    private static string[] Libelles(IEnumerable<IElement> liens) => [.. liens.Select(l => l.QuerySelector(".lt-nav-label")!.TextContent.Trim())];

    private bool AEcrit(string prefixe, string valeur) => _module.Invocations.Any(i => i.Identifier == "writePreference"
        && Equals(i.Arguments[0], prefixe + UserStorageKey.Hash(Utilisateur)) && Equals(i.Arguments[1], valeur));

    // ---------- pas de barre horizontale ----------

    [Fact]
    public void La_barre_laterale_ne_defile_qu_en_hauteur_et_les_libelles_longs_sont_tronques()
    {
        string coque = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "lettra.css"));
        string menu = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "lettra-nav.css"));

        coque.Should().Contain("@import url(\"lettra-nav.css\");");
        coque.Should().MatchRegex(@"\.lt-sidebar \{[^}]*overflow: hidden;", "la barre latérale ne défile plus en bloc");
        coque.Should().MatchRegex(@"\.lt-nav-label \{[^}]*min-width: 0;", "sans min-width, un libellé long élargit le lien au lieu d'être tronqué");
        menu.Should().MatchRegex(@"\.lt-nav-scroll \{[^}]*overflow-x: hidden;[^}]*overflow-y: auto;");
    }

    [Fact]
    public void L_aide_et_le_repli_restent_en_bas_hors_de_la_liste_qui_defile()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find(".lt-sidebar > .lt-sidebar-footer #nav-help").TextContent.Trim().Should().Be("Aide et questions");
        coque.Find(".lt-sidebar > .lt-sidebar-footer #nav-collapse").Should().NotBeNull();
        coque.FindAll(".lt-nav-scroll #nav-help, .lt-nav-scroll #nav-collapse").Should().BeEmpty();
    }

    // ---------- sections repliables ----------

    [Fact]
    public void Chaque_section_a_un_chevron_qui_la_replie_et_le_retient_pour_l_utilisateur()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.FindAll(".lt-sidebar .lt-nav-group").Select(g => g.TextContent.Trim()).Should().Equal("Ventes", "Tiers", "Comptabilité", "Pilotage");
        IElement bouton = Section(coque, "Nav.Group.Sales").QuerySelector(".lt-nav-group")!;
        bouton.GetAttribute("aria-expanded").Should().Be("true");
        bouton.QuerySelector(".lt-nav-chevron").Should().NotBeNull();

        bouton.Click();

        IElement ventes = Section(coque, "Nav.Group.Sales");
        ventes.ClassList.Should().Contain("is-collapsed");
        ventes.QuerySelector(".lt-nav-group")!.GetAttribute("aria-expanded").Should().Be("false");
        ventes.QuerySelector(".lt-nav-group")!.GetAttribute("aria-controls").Should().Be(ventes.QuerySelector(".lt-nav-items")!.Id);
        coque.WaitForAssertion(() => AEcrit("lettra.nav.sections.", "Nav.Group.Sales").Should().BeTrue());

        Section(coque, "Nav.Group.Sales").QuerySelector(".lt-nav-group")!.Click();
        Section(coque, "Nav.Group.Sales").ClassList.Should().NotContain("is-collapsed");
    }

    [Fact]
    public void Les_sections_repliees_sont_relues_au_demarrage()
    {
        _module.Setup<string?>("readPreference", "lettra.nav.sections." + UserStorageKey.Hash(Utilisateur)).SetResult("Nav.Group.Parties,Nav.Group.Settings");

        IRenderedComponent<MainLayout> coque = Coque();

        coque.WaitForAssertion(() => Section(coque, "Nav.Group.Parties").ClassList.Should().Contain("is-collapsed"));
        Section(coque, "Nav.Group.Settings").ClassList.Should().Contain("is-collapsed");
        Section(coque, "Nav.Group.Sales").ClassList.Should().NotContain("is-collapsed");
    }

    [Fact]
    public void Une_section_repliee_signale_l_ecran_affiche()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("quotes/new");
        IRenderedComponent<MainLayout> coque = Coque();
        Section(coque, "Nav.Group.Sales").QuerySelectorAll(".lt-nav-current-dot").Should().BeEmpty();

        Section(coque, "Nav.Group.Sales").QuerySelector(".lt-nav-group")!.Click();

        Section(coque, "Nav.Group.Sales").QuerySelector(".lt-nav-current-dot").Should().NotBeNull();
        Section(coque, "Nav.Group.Parties").QuerySelector(".lt-nav-group")!.Click();
        Section(coque, "Nav.Group.Parties").QuerySelectorAll(".lt-nav-current-dot").Should().BeEmpty();
    }

    // ---------- favoris ----------

    [Fact]
    public void L_etoile_epingle_un_ecran_dans_les_favoris_en_tete_du_menu()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.FindAll(".lt-sidebar .lt-nav-favorites").Should().BeEmpty();
        coque.FindAll(".lt-sidebar .lt-nav-section .lt-nav-pin").Should().NotContain(b => b.GetAttribute("aria-label") == "Épingler Accueil dans les favoris");

        Section(coque, "Nav.Group.Parties").QuerySelector("button[aria-label='Épingler Catalogue dans les favoris']")!.Click();
        Section(coque, "Nav.Group.Accounting").QuerySelector("button[aria-label='Épingler FEC dans les favoris']")!.Click();

        IElement favoris = coque.Find(".lt-sidebar .lt-nav-favorites");
        favoris.QuerySelector(".lt-nav-group")!.TextContent.Trim().Should().Be("Favoris");
        Libelles(favoris.QuerySelectorAll(".lt-nav-link")).Should().Equal("Catalogue", "FEC");
        favoris.QuerySelector(".lt-nav-pin")!.GetAttribute("aria-pressed").Should().Be("true");
        coque.Find(".lt-sidebar .lt-nav-scroll > section").ClassList.Should().Contain("lt-nav-favorites", "les favoris passent avant les sections");
        coque.Find(".lt-sidebar .lt-nav-scroll > ul + section").ClassList.Should().Contain("lt-nav-favorites", "juste après Accueil et Vue cabinet");
        coque.WaitForAssertion(() => AEcrit("lettra.nav.favorites.", "catalog,exports/fec").Should().BeTrue());

        coque.Find(".lt-sidebar .lt-nav-favorites button[aria-label='Retirer Catalogue des favoris']").Click();
        Libelles(coque.Find(".lt-sidebar .lt-nav-favorites").QuerySelectorAll(".lt-nav-link")).Should().Equal("FEC");
    }

    [Fact]
    public void Les_favoris_retenus_reviennent_et_un_ecran_disparu_est_ignore()
    {
        _module.Setup<string?>("readPreference", "lettra.nav.favorites." + UserStorageKey.Hash(Utilisateur)).SetResult("reminders,ecran-retire,customers");

        IRenderedComponent<MainLayout> coque = Coque();

        coque.WaitForAssertion(() => Libelles(coque.Find(".lt-sidebar .lt-nav-favorites").QuerySelectorAll(".lt-nav-link")).Should().Equal("Relances", "Clients"));
    }

    // ---------- recherche du menu ----------

    [Theory]
    [InlineData("tva", "Déclarations")]
    [InlineData("recurrent", "Factures récurrentes")]
    [InlineData("banque", "Rapprochement bancaire")]
    [InlineData("impaye", "Relances")]
    public void La_recherche_trouve_un_ecran_sans_accents_et_par_ses_mots_cles(string tape, string premier)
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input(tape);

        Libelles(coque.FindAll(".lt-sidebar .lt-nav-results .lt-nav-link")).First().Should().Be(premier);
        coque.FindAll(".lt-sidebar .lt-nav-section").Should().BeEmpty("les résultats remplacent les sections");
    }

    [Fact]
    public void Un_resultat_rappelle_sa_section_et_le_libelle_passe_devant_les_mots_cles()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input("factur");

        IElement[] resultats = [.. coque.FindAll(".lt-sidebar .lt-nav-results .lt-nav-link")];
        Libelles(resultats).Take(2).Should().Equal("Factures", "Factures récurrentes");
        resultats[0].QuerySelector(".lt-nav-hint")!.TextContent.Should().Be("Ventes");
        coque.Find(".lt-sidebar [role=status]").TextContent.Should().StartWith(resultats.Length.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Entree_ouvre_le_premier_ecran_et_echap_efface_la_recherche()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input("fourn");
        coque.Find("#nav-filter").KeyDown("Enter");

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/suppliers");
        coque.Find("#nav-filter").GetAttribute("value").Should().BeEmpty();
        coque.FindAll(".lt-sidebar .lt-nav-section").Should().NotBeEmpty();

        coque.Find("#nav-filter").Input("cata");
        coque.Find("#nav-filter").KeyDown("Escape");
        coque.FindAll(".lt-sidebar .lt-nav-results").Should().BeEmpty();
    }

    [Fact]
    public void Sans_resultat_le_menu_propose_de_chercher_partout()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input("Dupont");

        coque.Find(".lt-sidebar .lt-nav-empty").TextContent.Should().Be("Aucun écran ne correspond à «\u00A0Dupont\u00A0».");
        coque.Find("#nav-everywhere").TextContent.Trim().Should().Be("Chercher «\u00A0Dupont\u00A0» partout");
        coque.Find("#nav-everywhere").Click();

        coque.WaitForAssertion(() => coque.Find("#palette-input").GetAttribute("value").Should().Be("Dupont"));
        coque.Find("#nav-filter").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void Sans_resultat_la_question_peut_passer_a_l_aide()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input("corriger une facture");
        coque.Find("#nav-ask").Click();

        coque.Find("#help-panel").GetAttribute("role").Should().Be("dialog");
        coque.Find("#help-query").GetAttribute("value").Should().Be("corriger une facture");
        coque.Find("#help-faq-fix-invoice").HasAttribute("open").Should().BeTrue();
    }

    [Fact]
    public void Entree_sans_resultat_ouvre_la_palette()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#nav-filter").Input("FAC-2026-0042");
        coque.Find("#nav-filter").KeyDown("Enter");

        coque.WaitForAssertion(() => coque.Find("#palette-input").GetAttribute("value").Should().Be("FAC-2026-0042"));
    }

    [Fact]
    public void En_rail_la_loupe_ouvre_la_palette()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find(".lt-sidebar .lt-nav-search-rail").Click();

        coque.WaitForAssertion(() => coque.FindAll("#palette-input").Should().ContainSingle());
    }

    // ---------- téléphone ----------

    [Fact]
    public void La_feuille_plus_a_sa_recherche_ses_sections_et_l_aide()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tab-more").Click();

        coque.Find("#more-filter").Input("fac");
        Libelles(coque.FindAll("#more-sheet .lt-nav-results .lt-nav-link")).Should().StartWith(["Factures", "Factures récurrentes"], "la recherche couvre aussi les onglets du bas");
        coque.Find("#more-filter").KeyDown("Escape");
        coque.FindAll("#more-sheet .lt-nav-section").Select(s => s.GetAttribute("data-group"))
            .Should().Equal("Nav.Group.Sales", "Nav.Group.Parties", "Nav.Group.Accounting", "Nav.Group.Settings");

        coque.Find("#more-help").Click();

        coque.FindAll("#more-sheet").Should().BeEmpty();
        coque.FindAll("#help-panel").Should().ContainSingle();
    }

    [Fact]
    public void Un_resultat_choisi_dans_la_feuille_la_referme()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tab-more").Click();

        coque.Find("#more-filter").Input("fec");
        coque.Find("#more-filter").KeyDown("Enter");

        coque.FindAll("#more-sheet").Should().BeEmpty();
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/exports/fec");
    }
}
