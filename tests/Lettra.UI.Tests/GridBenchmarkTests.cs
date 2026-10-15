using System.Globalization;
using System.Xml.Linq;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components;
using Lettra.UI.Shared.Diagnostics;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

public sealed class LatencyStatsTests
{
    [Fact]
    public void Le_p95_est_pris_au_rang_le_plus_proche()
    {
        double[] mesures = [.. Enumerable.Range(1, 100).Select(i => (double)i).Reverse()];

        LatencyStats stats = LatencyStats.From(mesures);

        stats.Should().Be(new LatencyStats(100, 50, 95, 100));
    }

    [Fact]
    public void Une_mesure_lente_isolee_ne_fait_pas_le_p95_de_vingt_frappes()
    {
        double[] mesures = [.. Enumerable.Repeat(10d, 19), 400d];

        LatencyStats.From(mesures).P95.Should().Be(10);
        LatencyStats.From([.. mesures, 400d]).P95.Should().Be(400, "deux lenteurs sur vingt et une dépassent les 5 %");
    }

    [Fact]
    public void Une_serie_vide_donne_des_zeros()
    {
        LatencyStats.From([]).Should().Be(new LatencyStats(0, 0, 0, 0));
    }

    [Fact]
    public void Les_millisecondes_sont_arrondies_sans_dependre_de_la_culture()
    {
        LatencyStats.Ms(12.5).Should().Be("13 ms");
        LatencyStats.Ms(0.4).Should().Be("0 ms");
    }
}

public sealed class DiagnosticsResourceTests
{
    [Fact]
    public void Les_textes_de_la_mesure_existent_en_francais_et_en_anglais()
    {
        string dossier = Path.Combine(RepositoryRoot(), "src", "Presentation", "Lettra.UI.Shared", "Diagnostics");

        HashSet<string> francais = Keys(Path.Combine(dossier, "DiagnosticsResource.resx"));
        HashSet<string> anglais = Keys(Path.Combine(dossier, "DiagnosticsResource.en.resx"));

        francais.Should().NotBeEmpty().And.BeEquivalentTo(anglais);
    }

    private static HashSet<string> Keys(string path) =>
        [.. XDocument.Load(path).Root!.Elements("data").Select(d => (string)d.Attribute("name")!)];

    private static string RepositoryRoot()
    {
        DirectoryInfo? dossier = new(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "Directory.Packages.props")))
        {
            dossier = dossier.Parent;
        }

        return dossier?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }
}

[Collection("culture")]
public sealed class GridBenchmarkPageTests : BunitContext
{
    public GridBenchmarkPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void La_page_affiche_la_piece_de_mesure_sans_lancer_la_mesure()
    {
        IRenderedComponent<GridBenchmark> page = Render<GridBenchmark>();

        page.Find("h1").TextContent.Should().Be("Mesure de la grille");
        page.FindAll(".lg-grid").Should().ContainSingle();
        page.FindAll("#bench-results").Should().BeEmpty();
        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "run");
    }

    [Fact]
    public void Le_resultat_s_affiche_avec_le_texte_a_coller()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule("./_content/Lettra.UI.Shared/grid-benchmark.js");
        double[] navigation = [.. Enumerable.Range(1, 100).Select(i => (double)i)];
        module.Setup<GridBenchmarkRun>("run", _ => true).SetResult(new GridBenchmarkRun(navigation, [20, 30], [40, 50], 1, 37));
        module.Setup<GridBenchmarkEnvironment>("environment").SetResult(new GridBenchmarkEnvironment("Navigateur de test", 8, 1920, 1080, 1.25));
        Services.GetRequiredService<NavigationManager>().NavigateTo("mesure-grille?auto=1");

        IRenderedComponent<GridBenchmark> page = Render<GridBenchmark>();

        page.WaitForAssertion(() => page.Find("#bench-results"), TimeSpan.FromSeconds(5));
        page.FindAll("#bench-results tbody tr").Should().HaveCount(3);
        string texte = page.Find("#bench-text").TextContent;
        texte.Should().Contain("Navigation p95 = 95 ms (médiane 50 ms, max 100 ms, n = 100)")
            .And.Contain("8 cœurs, fenêtre 1920×1080 @1.25")
            .And.Contain("Sans réponse : 1");
        page.Markup.Should().Contain("1 frappe(s) sans réponse");
    }

    [Fact]
    public void Une_erreur_de_la_mesure_s_affiche_sans_arreter_l_application()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule("./_content/Lettra.UI.Shared/grid-benchmark.js");
        module.Setup<GridBenchmarkRun>("run", _ => true).SetException(new InvalidOperationException("grille absente"));
        Services.GetRequiredService<NavigationManager>().NavigateTo("mesure-grille?auto=1");

        IRenderedComponent<GridBenchmark> page = Render<GridBenchmark>();

        page.WaitForAssertion(() => page.Find("p[role=alert]").TextContent.Should().Contain("InvalidOperationException : grille absente"), TimeSpan.FromSeconds(5));
        page.Find("#bench-start").HasAttribute("disabled").Should().BeFalse("la mesure peut être relancée");
    }

    [Fact]
    public void Sans_compte_local_la_mesure_reste_affichee()
    {
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(false, SessionModeDto.Local, null, LocalAccountExists: false)));
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("mesure-grille?auto=1");

        Render<SessionBar>();

        navigation.Uri.Should().Be("http://localhost/mesure-grille?auto=1", "la version de mesure s'ouvre avant tout compte local");
        navigation.NavigateTo("quotes");
        navigation.Uri.Should().Be("http://localhost/session");
    }
}
