using System.Globalization;
using Bunit;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Demo;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Demo;
using Lettra.UI.Tests.Design;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using static Lettra.UI.Shared.Demo.DemoAction;

namespace Lettra.UI.Tests.Demo;

/// <summary>Écran « Démo commentée » et bulle de lecture : disponibilité, chapitres, commandes, palette.</summary>
[Collection("culture")]
public sealed class DemoScreensTests : BunitContext
{
    private readonly FakeDemoDriver _driver = new();

    public DemoScreensTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddScoped<IDemoDriver>(_ => _driver);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private DemoPlayer Player => Services.GetRequiredService<DemoPlayer>();

    private void Disponible(bool available) => Services.AddSingleton<IDemoDataGateway>(new FakeDemoDataGateway(available, () => { }));

    // Scénario court, mis en pause dès la première bulle pour regarder la bulle à loisir.
    private void ScenarioEnPause()
    {
        Player.Use(DemoScriptCatalog.Chapters.Take(2).ToList());
        Player.Delay = (_, _) => Task.CompletedTask;
        _driver.OnHighlight = _ =>
        {
            _driver.OnHighlight = null;
            Player.Pause();
        };
    }

    [Fact]
    public void L_ecran_explique_que_la_demo_n_existe_que_sur_une_installation_de_demonstration()
    {
        Disponible(false);

        IRenderedComponent<DemoPage> page = Render<DemoPage>();

        page.Find("#demo-unavailable").TextContent.Should().Contain("installation de démonstration");
        page.FindAll("#demo-start").Should().BeEmpty();
    }

    [Fact]
    public void Sans_jeu_de_demonstration_l_ecran_ne_propose_rien()
    {
        IRenderedComponent<DemoPage> page = Render<DemoPage>();

        page.FindAll("#demo-unavailable").Should().ContainSingle();
    }

    [Fact]
    public void L_ecran_liste_les_chapitres_et_lance_la_demo()
    {
        Disponible(true);
        ScenarioEnPause();

        IRenderedComponent<DemoPage> page = Render<DemoPage>();

        page.FindAll(".lt-demo-chapter-card").Should().HaveCount(2);
        page.Find("#demo-start").Click();

        Player.State.Should().Be(DemoState.Paused);
        _driver.Gestures[0].Should().Be("go ");
        page.WaitForAssertion(() => page.Find("#demo-start").TextContent.Should().Contain("Relancer"));
        page.Find(".lt-demo-chapter-card.is-current h3").TextContent.Should().Contain("Bienvenue");
    }

    [Fact]
    public void Un_chapitre_qui_continue_le_precedent_ne_se_lance_pas_seul()
    {
        Disponible(true);

        IRenderedComponent<DemoPage> page = Render<DemoPage>();

        page.FindAll(".lt-demo-chapter-card").Should().HaveCount(DemoScriptCatalog.Chapters.Count);
        page.FindAll("#demo-chapter-invoice").Should().BeEmpty();
        page.Find("#demo-chapter-invoice-continues").TextContent.Should().Contain("chapitre précédent");
        page.Find("#demo-chapter-quote").GetAttribute("aria-label").Should().Contain("Faire un devis");
    }

    [Fact]
    public void Commencer_a_un_chapitre_et_choisir_la_vitesse()
    {
        Disponible(true);
        ScenarioEnPause();

        IRenderedComponent<DemoPage> page = Render<DemoPage>();
        page.Find("#demo-page-speed").Change("1.5");
        page.Find("#demo-chapter-company").Click();

        Player.Speed.Should().Be(1.5);
        Player.Chapter!.Id.Should().Be("company");
        _driver.Gestures[0].Should().Be("go settings/company");
    }

    [Fact]
    public void La_bulle_montre_l_etape_et_ses_commandes()
    {
        ScenarioEnPause();
        IRenderedComponent<DemoHost> host = Render<DemoHost>();
        host.FindAll("#demo-card").Should().BeEmpty();

        _ = Player.StartAsync();

        host.WaitForAssertion(() => host.Find("#demo-step-title").TextContent.Should().Be("Bienvenue dans Lettra"));
        host.Find("#demo-count").TextContent.Should().Be("Chapitre 1 sur 2");
        host.Find("#demo-paused");
        host.Find("#demo-live").TextContent.Should().StartWith("Bienvenue dans Lettra. Lettra sert");

        host.Find("#demo-resume").Click();
        host.WaitForAssertion(() => Player.State.Should().Be(DemoState.Finished));
        host.Find("#demo-step-title").TextContent.Should().Be("Démo terminée, bravo !");
        host.Find("#demo-live").TextContent.Should().Be("Démo terminée, bravo !");
        host.Find("[role=progressbar]").GetAttribute("aria-valuenow").Should().Be("100");

        host.Find("#demo-close").Click();
        host.WaitForAssertion(() => host.FindAll("#demo-card").Should().BeEmpty());
        Player.State.Should().Be(DemoState.Idle);
    }

    [Fact]
    public void Pause_suivant_vitesse_et_reduction_depuis_la_bulle()
    {
        ScenarioEnPause();
        IRenderedComponent<DemoHost> host = Render<DemoHost>();
        _ = Player.StartAsync();
        host.WaitForAssertion(() => host.Find("#demo-resume"));

        host.Find("#demo-speed").Change("2");
        Player.Speed.Should().Be(2);
        host.Find("#demo-speed").Change("abc");
        Player.Speed.Should().Be(2);

        host.Find("#demo-collapse").Click();
        host.Find("#demo-card").ClassList.Should().Contain("is-collapsed");
        host.Find("#demo-collapse").GetAttribute("aria-expanded").Should().Be("false");
        host.Find("#demo-collapse").Click();

        _driver.OnHighlight = _ =>
        {
            _driver.OnHighlight = null;
            Player.Pause();
        };
        host.Find("#demo-next").Click();
        // La pause part du surlignage de l'étape 1 : attendre que la bulle l'affiche (bouton Reprendre) avant de lire.
        host.WaitForAssertion(() =>
        {
            Player.StepIndex.Should().Be(1);
            host.Find("#demo-resume");
        });
        host.Find("#demo-step-title").TextContent.Should().Be("Un dossier, c'est une entreprise");
        Player.Delay = (_, token) => Task.Delay(Timeout.Infinite, token);   // la lecture dure : le bouton Pause reste là
        host.Find("#demo-resume").Click();
        host.WaitForAssertion(() => host.Find("#demo-pause"));
        host.Find("#demo-pause").Click();
        Player.State.Should().Be(DemoState.Paused);

        host.Find("#demo-stop").Click();
        host.WaitForAssertion(() => host.FindAll("#demo-card").Should().BeEmpty());
    }

    [Fact]
    public void Une_etape_bloquee_propose_de_reessayer_ou_de_passer()
    {
        Player.Use([new("customer", "users", [new("list", "#customers-list", Go("customers"), Click("#absent")), new("new", null)])]);
        Player.Delay = (_, _) => Task.CompletedTask;
        _driver.Missing.Add("#absent");
        IRenderedComponent<DemoHost> host = Render<DemoHost>();

        _ = Player.StartAsync();

        host.WaitForAssertion(() => host.Find("#demo-blocked"));
        host.Find("#demo-card").ClassList.Should().Contain("is-blocked");
        host.Find("#demo-live").TextContent.Should().Contain("pas trouvé");
        host.Find("#demo-retry").Click();
        host.WaitForAssertion(() => host.Find("#demo-skip"));
        host.Find("#demo-skip").Click();
        host.WaitForAssertion(() => host.Find("#demo-replay"));

        _driver.Missing.Clear();
        host.Find("#demo-replay").Click();
        host.WaitForAssertion(() => _driver.Gestures.Count(g => g == "go customers").Should().Be(3));
    }

    [Fact]
    public void Pendant_la_lecture_la_bulle_annonce_les_gestes_en_cours()
    {
        TaskCompletionSource attente = new();
        Player.Use([new("customer", "users", [new("list", "#customers-list", Go("customers"), Wait("#customers-list"))])]);
        Player.Delay = (_, _) => attente.Task;
        IRenderedComponent<DemoHost> host = Render<DemoHost>();

        _ = Player.StartAsync();

        host.WaitForAssertion(() => host.Find("#demo-acting"));
        host.Find("#demo-live").TextContent.Should().Contain("La liste des clients");
        host.Find(".lt-demo-chapter").TextContent.Should().Contain("Créer un client");
        attente.SetResult();
    }

    [Fact]
    public async Task La_palette_ouvre_la_demo_et_l_arrete_pendant_la_lecture()
    {
        ScenarioEnPause();
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        DemoPaletteProvider palette = new(Player, navigation, Services.GetRequiredService<IStringLocalizer<SharedResource>>());

        IReadOnlyList<PaletteItem> items = await palette.SearchAsync("démo", CancellationToken.None);
        items.Should().ContainSingle();
        await items[0].Run();
        navigation.Uri.Should().EndWith("/demo");

        _ = Player.StartAsync();
        items = await palette.SearchAsync("arrêter", CancellationToken.None);
        items.Should().ContainSingle();
        await items[0].Run();
        Player.State.Should().Be(DemoState.Idle);
    }
}
