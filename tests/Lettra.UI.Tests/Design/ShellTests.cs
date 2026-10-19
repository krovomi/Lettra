using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Demo;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
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

/// <summary>Coque de l'application (vague 5, lane B) : navigation par appareil, apparence et tableau de bord d'accueil.</summary>
[Collection("culture")]
public sealed class ShellTests : BunitContext
{
    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };

    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly BunitJSModuleInterop _module;

    public ShellTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule(UiModule.Path);
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<MainLayout> Coque() => Render<MainLayout>(p => p.Add(l => l.Body, b =>
    {
        b.OpenComponent<Home>(0);
        b.CloseComponent();
    }));

    private static string[] Libelles(IEnumerable<IElement> liens) => [.. liens.Select(l => l.TextContent.Trim())];

    [Fact]
    public void La_barre_laterale_groupe_les_ecrans_et_les_onglets_du_telephone_gardent_l_essentiel()
    {
        IRenderedComponent<MainLayout> coque = Coque();

        Libelles(coque.FindAll(".lt-sidebar .lt-nav-link")).Should().Equal(
            "Accueil", "Vue cabinet", "Devis", "Factures", "Avoirs", "Factures récurrentes", "Clients", "Fournisseurs", "Catalogue", "Encaissements", "Rapprochement bancaire", "Relances", "Journal et balance", "FEC", "Déclarations", "Revue du dossier", "Tableaux de bord", "Alertes", "Entreprise", "Mes préférences", "Serveur du cabinet");
        coque.FindAll(".lt-sidebar .lt-nav-group").Select(g => g.TextContent).Should().Equal("Ventes", "Tiers", "Comptabilité", "Pilotage");
        Libelles(coque.FindAll(".lt-tabbar .lt-tab")).Should().Equal("Accueil", "Devis", "Factures", "Clients", "Plus");
        coque.Find(".lt-skip").GetAttribute("href").Should().Be("#main");
    }

    [Fact]
    public void La_feuille_plus_porte_le_reste_de_la_navigation_et_l_apparence_puis_se_ferme_avec_echap()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.FindAll("#more-sheet").Should().BeEmpty();

        coque.Find("#tab-more").Click();

        coque.Find("#tab-more").GetAttribute("aria-expanded").Should().Be("true");
        IElement feuille = coque.Find("#more-sheet");
        feuille.GetAttribute("role").Should().Be("dialog");
        Libelles(feuille.QuerySelectorAll(".lt-nav-link")).Should().Equal("Vue cabinet", "Avoirs", "Factures récurrentes", "Fournisseurs", "Catalogue", "Encaissements", "Rapprochement bancaire", "Relances", "Journal et balance", "FEC", "Déclarations", "Revue du dossier", "Tableaux de bord", "Alertes", "Entreprise", "Mes préférences", "Serveur du cabinet");
        feuille.QuerySelectorAll("input[type=radio][name^=lettra-theme]").Should().HaveCount(5);

        feuille.KeyDown("Escape");
        coque.FindAll("#more-sheet").Should().BeEmpty();
    }

    [Fact]
    public void Changer_de_theme_l_applique_sans_recharger_et_le_retient_sur_l_appareil()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#appearance-toggle").Click();
        coque.FindAll("#appearance-panel input[name^=lettra-theme]").Select(r => r.ParentElement!.TextContent.Trim())
            .Should().Equal("Comme le système", "Clair", "Sombre", "Ardoise", "Contraste élevé");

        coque.Find("#appearance-panel input[value=Dark]").Change(true);

        _module.Invocations.Should().ContainSingle(i => i.Identifier == "applyTheme").Which.Arguments.Should().Equal("dark");
        _module.Invocations.Should().Contain(i => i.Identifier == "writePreference" && Equals(i.Arguments[0], "lettra.theme") && Equals(i.Arguments[1], "Dark"));
        coque.Find("#appearance-panel input[value=Dark]").HasAttribute("checked").Should().BeTrue();

        coque.Find("#appearance-panel input[value=System]").Change(true);
        _module.Invocations.Last(i => i.Identifier == "applyTheme").Arguments.Should().Equal([null], "suivre le système retire data-theme");
    }

    [Fact]
    public void Le_theme_retenu_est_reapplique_au_demarrage()
    {
        _module.Setup<string?>("readPreference", "lettra.theme").SetResult("Slate");
        _module.Setup<string?>("readPreference", "lettra.nav").SetResult("collapsed");

        IRenderedComponent<MainLayout> coque = Coque();

        coque.WaitForAssertion(() => coque.Find(".lt-shell").GetAttribute("data-nav").Should().Be("collapsed"));
        _module.Invocations.Should().ContainSingle(i => i.Identifier == "applyTheme").Which.Arguments.Should().Equal("slate");
        coque.Find("#nav-collapse").GetAttribute("aria-label").Should().Be("Déplier le menu");
    }

    [Fact]
    public void Replier_la_barre_laterale_la_passe_en_rail_et_le_retient()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find(".lt-shell").GetAttribute("data-nav").Should().Be("expanded");

        coque.Find("#nav-collapse").Click();

        coque.Find(".lt-shell").GetAttribute("data-nav").Should().Be("collapsed");
        coque.Find("#nav-collapse").GetAttribute("aria-pressed").Should().Be("true");
        _module.Invocations.Should().Contain(i => i.Identifier == "writePreference" && Equals(i.Arguments[0], "lettra.nav") && Equals(i.Arguments[1], "collapsed"));
    }

    [Fact]
    public void Chaque_entree_de_navigation_mene_a_un_ecran_existant()
    {
        HashSet<string> routes =
        [
            .. typeof(MainLayout).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
                .Select(r => r.Template.Trim('/')),
        ];

        NavItems.All.Select(i => i.Href).Should().OnlyContain(href => routes.Contains(href));
    }

    [Fact]
    public async Task La_palette_propose_chaque_theme()
    {
        NavigationPaletteProvider palette = Services.GetServices<ICommandPaletteProvider>().OfType<NavigationPaletteProvider>().Single();

        IReadOnlyList<PaletteItem> items = await palette.SearchAsync("thème", CancellationToken.None);

        items.Select(i => i.Label).Should().Contain(["Thème : Sombre", "Thème : Contraste élevé", "Thème : Comme le système"]);
    }

    [Fact]
    public void L_accueil_resume_le_mois_et_les_dernieres_pieces()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Conseil);
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Conseil);
        _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0007", Conseil, Conseil);
        _quotes.Add(QuoteStatusDto.Draft, null, Conseil);

        IRenderedComponent<Home> accueil = Render<Home>();

        accueil.WaitForAssertion(() => accueil.Find("#kpi-revenue .lt-kpi-value").TextContent.Should().Be("200 €"));
        accueil.Find("#kpi-revenue .lt-kpi-note").TextContent.Trim().Should().Be("Aucune facture le mois dernier");
        accueil.Find("#kpi-invoices .lt-kpi-value").TextContent.Should().Be("1");
        accueil.Find("#kpi-pending .lt-kpi-value").TextContent.Should().Be("1");
        accueil.Find("#kpi-pending .lt-kpi-note").TextContent.Should().Be("400 € HT en attente de réponse");
        accueil.Find("#kpi-drafts .lt-kpi-value").TextContent.Should().Be("2");
        accueil.FindAll("#home-chart .lt-bar").Should().HaveCount(6);
        accueil.Find("#home-chart .lt-bar.is-current .lt-bar-label").TextContent.Should().Be("oct.");
        accueil.Find("#home-recent-invoices .lt-list-meta").TextContent.Should().Be("FAC-2026-0042 · 15/10/2026");
        accueil.Find("#home-recent-quotes .lt-list-title").TextContent.Should().Be("Dupont SARL");
        accueil.FindAll("#home-empty").Should().BeEmpty();
    }

    [Fact]
    public void Un_dossier_vide_invite_a_commencer()
    {
        IRenderedComponent<Home> accueil = Render<Home>();

        accueil.WaitForAssertion(() => accueil.Find("#home-empty h2").TextContent.Should().Be("Bienvenue dans Lettra"));
        accueil.FindAll("#home-empty a").Select(a => a.GetAttribute("href")).Should().Equal("settings/company", "customers", "quotes/new");
        accueil.FindAll(".lt-kpi").Should().BeEmpty();
    }

    [Fact]
    public void L_accueil_montre_les_factures_a_relancer_et_les_transmissions()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Conseil);
        Guid retard = Guid.CreateVersion7();
        Services.AddSingleton<IReceivablesGateway>(new FakeReceivablesGateway(new ReceivablesSummaryResponse(
            new DateOnly(2026, 10, 16), 3, 4250m,
            [new OverdueInvoiceResponse(retard, "FAC-2026-0007", "Atelier Durand", new DateOnly(2026, 9, 1), 45, 3000m)],
            2, 5, 1, 7)));

        IRenderedComponent<Home> accueil = Render<Home>();

        accueil.WaitForAssertion(() => accueil.Find("#kpi-overdue .lt-kpi-value").TextContent.Should().Be("4\u00A0250 €"));
        accueil.Find("#kpi-overdue").ClassList.Should().Contain("is-alert");
        accueil.Find("#kpi-overdue .lt-kpi-note").TextContent.Should().Be("3 à relancer, TTC restant dû");
        accueil.FindAll("#kpi-invoices").Should().BeEmpty("le retard remplace le nombre de factures du mois");
        accueil.Find("#home-overdue .lt-list a").GetAttribute("href").Should().Be($"invoices/{retard}");
        accueil.Find("#home-overdue .lt-list-meta").TextContent.Should().Be("FAC-2026-0007 · échue depuis 45 j");
        accueil.FindAll("#home-transmissions dd").Select(d => d.TextContent).Should().Equal("2", "5", "1", "7");
        accueil.Find("#home-transmissions [data-stat=needs-attention]").ClassList.Should().Contain("is-alert");
    }

    [Fact]
    public void Sans_point_des_creances_l_accueil_garde_les_factures_du_mois()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Conseil);
        Services.AddSingleton<IReceivablesGateway>(new FakeReceivablesGateway(null));

        IRenderedComponent<Home> accueil = Render<Home>();

        accueil.WaitForAssertion(() => accueil.Find("#kpi-invoices .lt-kpi-value").TextContent.Should().Be("1"));
        accueil.FindAll("#kpi-overdue, #home-overdue, #home-transmissions").Should().BeEmpty();
    }

    [Fact]
    public void Un_dossier_vide_charge_les_donnees_de_demonstration_puis_affiche_le_tableau_de_bord()
    {
        FakeDemoDataGateway demo = new(true, () => _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0001", Conseil));
        Services.AddSingleton<IDemoDataGateway>(demo);

        IRenderedComponent<Home> accueil = Render<Home>();
        accueil.WaitForAssertion(() => accueil.Find("#home-demo-load").TextContent.Trim().Should().Be("Charger les données de démonstration"));
        accueil.Find("#home-demo-load").Click();

        accueil.WaitForAssertion(() => accueil.Find("#kpi-revenue .lt-kpi-value").TextContent.Should().Be("200 €"));
        demo.Seeds.Should().Be(1);
        accueil.FindAll("#home-empty, #home-demo").Should().BeEmpty();
        Services.GetRequiredService<ToastService>().Visible.Should().ContainSingle()
            .Which.Should().Be(new Toast(1, ToastKind.Success, "Données de démonstration chargées : 12 clients, 30 devis et 1 factures."));
    }

    [Fact]
    public void Le_bouton_de_demonstration_n_apparait_que_si_l_hote_le_permet()
    {
        Services.AddSingleton<IDemoDataGateway>(new FakeDemoDataGateway(false, () => { }));

        IRenderedComponent<Home> accueil = Render<Home>();

        accueil.WaitForAssertion(() => accueil.Find("#home-empty h2").TextContent.Should().Be("Bienvenue dans Lettra"));
        accueil.FindAll("#home-demo").Should().BeEmpty();
    }

    [Fact]
    public void Un_dossier_deja_rempli_entre_temps_est_signale_sans_rien_charger()
    {
        FakeDemoDataGateway demo = new(true, () => { }, DemoSeedStatusDto.DossierNotEmpty);
        Services.AddSingleton<IDemoDataGateway>(demo);

        IRenderedComponent<Home> accueil = Render<Home>();
        accueil.WaitForAssertion(() => accueil.Find("#home-demo-load").Click());

        accueil.WaitForAssertion(() => Services.GetRequiredService<ToastService>().Visible.Should().ContainSingle()
            .Which.Kind.Should().Be(ToastKind.Warning));
        accueil.Find("#home-demo-load").HasAttribute("disabled").Should().BeFalse();
    }
}
