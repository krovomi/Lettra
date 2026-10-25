using System.Globalization;
using Bunit;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Demo;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Demo;

/// <summary>Le scénario de la démo commentée : textes traduits, chapitres qui se lancent seuls, valeurs tapées.</summary>
[Collection("culture")]
public sealed class DemoScriptCatalogTests : BunitContext
{
    public DemoScriptCatalogTests() => Services.AddLettraUi();

    private IStringLocalizer<SharedResource> Localizer(string culture)
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo(culture));
        return Services.GetRequiredService<IStringLocalizer<SharedResource>>();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Chaque_chapitre_et_chaque_etape_ont_leurs_textes_traduits(string culture)
    {
        IStringLocalizer<SharedResource> l = Localizer(culture);
        List<string> keys = ["Nav.Demo", "Nav.Demo.Keywords", .. DemoPlayer.Speeds.Select(s => $"Demo.Speed.{s.ToString("0.##", CultureInfo.InvariantCulture)}")];
        foreach (DemoChapter chapter in DemoScriptCatalog.Chapters)
        {
            keys.Add(chapter.TitleKey);
            keys.Add(chapter.SummaryKey);
            keys.AddRange(chapter.Steps.SelectMany(s => new[] { chapter.StepTitleKey(s), chapter.StepTextKey(s) }));
        }

        foreach (string key in keys)
        {
            l[key].ResourceNotFound.Should().BeFalse(key);
            l[key].Value.Should().NotBeNullOrWhiteSpace(key);
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Les_valeurs_tapees_lues_dans_les_ressources_existent(string culture)
    {
        IStringLocalizer<SharedResource> l = Localizer(culture);
        IEnumerable<string> values = DemoScriptCatalog.Chapters.SelectMany(c => c.Steps).SelectMany(s => s.Actions)
            .Select(a => a.Value).OfType<string>().Where(v => v.StartsWith(DemoValues.ResourcePrefix, StringComparison.Ordinal));

        values.Should().NotBeEmpty();
        foreach (string value in values)
        {
            l[value[DemoValues.ResourcePrefix.Length..]].ResourceNotFound.Should().BeFalse(value);
        }
    }

    [Fact]
    public void Les_identifiants_sont_uniques_et_le_parcours_couvre_toute_la_vente()
    {
        IReadOnlyList<DemoChapter> chapters = DemoScriptCatalog.Chapters;

        chapters.Select(c => c.Id).Should().OnlyHaveUniqueItems();
        chapters.Should().AllSatisfy(c => c.Steps.Select(s => s.Id).Should().OnlyHaveUniqueItems());
        chapters.Select(c => c.Id).Should().ContainInOrder("customer", "catalog", "quote", "invoice", "payment", "bank", "reminders", "accounting", "tax", "fec", "cabinet");
        DemoScriptCatalog.Find("quote").Should().NotBeNull();
        DemoScriptCatalog.Find("inconnu").Should().BeNull();
    }

    [Fact]
    public void Un_chapitre_commence_par_ouvrir_son_ecran_sauf_s_il_continue_le_precedent()
    {
        DemoScriptCatalog.Chapters[0].ContinuesPrevious.Should().BeFalse();
        foreach (DemoChapter chapter in DemoScriptCatalog.Chapters.Where(c => !c.ContinuesPrevious))
        {
            chapter.Steps[0].Actions.Should().NotBeEmpty(chapter.Id);
            chapter.Steps[0].Actions[0].Kind.Should().Be(DemoActionKind.Navigate, chapter.Id);
        }

        DemoScriptCatalog.Find("invoice")!.ContinuesPrevious.Should().BeTrue();
    }

    [Fact]
    public void Chaque_ecran_ouvert_par_la_demo_est_une_route_connue()
    {
        IEnumerable<string> routes = DemoScriptCatalog.Chapters.SelectMany(c => c.Steps).SelectMany(s => s.Actions)
            .Where(a => a.Kind == DemoActionKind.Navigate).Select(a => a.Value!);
        string[] pages = [.. typeof(DemoPlayer).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false))
            .Cast<Microsoft.AspNetCore.Components.RouteAttribute>().Select(r => r.Template.Trim('/'))];

        routes.Should().AllSatisfy(r => pages.Should().Contain(r));
        NavItems.All.Should().Contain(i => i.Href == "demo");
        pages.Should().Contain("demo");
    }
}
