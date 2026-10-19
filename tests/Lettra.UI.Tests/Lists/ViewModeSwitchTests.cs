using System.Globalization;
using Bunit;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Lists;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Lists;

/// <summary>Bascule liste / saisie en grille : les deux modes restent toujours accessibles, la vue en cours est marquée.</summary>
[Collection("culture")]
public sealed class ViewModeSwitchTests : BunitContext
{
    public ViewModeSwitchTests()
    {
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<ViewModeSwitch> RenderSwitch(ViewMode current) =>
        Render<ViewModeSwitch>(p => p
            .Add(c => c.Current, current)
            .Add(c => c.ListHref, "things")
            .Add(c => c.GridHref, "things/grid")
            .Add(c => c.Id, "things-view"));

    [Theory]
    [InlineData(ViewMode.List, "things-view-list", "things-view-grid")]
    [InlineData(ViewMode.Grid, "things-view-grid", "things-view-list")]
    public void Les_deux_modes_sont_des_liens_et_seul_le_mode_en_cours_est_marque(ViewMode current, string on, string off)
    {
        IRenderedComponent<ViewModeSwitch> view = RenderSwitch(current);

        view.Find($"#{on}").GetAttribute("aria-current").Should().Be("page");
        view.Find($"#{off}").HasAttribute("aria-current").Should().BeFalse();
        view.Find("#things-view-list").GetAttribute("href").Should().Be("things");
        view.Find("#things-view-grid").GetAttribute("href").Should().Be("things/grid");
    }

    [Fact]
    public void Les_libelles_suivent_la_langue()
    {
        Verifier(RenderSwitch(ViewMode.List), "Affichage", "Liste", "Saisie en grille");

        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        Verifier(RenderSwitch(ViewMode.List), "View", "List", "Grid entry");

        static void Verifier(IRenderedComponent<ViewModeSwitch> v, string label, string list, string grid)
        {
            v.Find("nav").GetAttribute("aria-label").Should().Be(label);
            v.Find("#things-view-list").TextContent.Trim().Should().Be(list);
            v.Find("#things-view-grid").TextContent.Trim().Should().Be(grid);
        }
    }
}
