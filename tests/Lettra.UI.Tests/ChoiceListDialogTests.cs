using System.Globalization;
using Bunit;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Localization;

namespace Lettra.UI.Tests;

[Collection("culture")]
public sealed class ChoiceListDialogTests : BunitContext
{
    private static readonly string[] Unites = ["Heure", "Jour", "Mois", "Forfait"];

    private string? _choisi;
    private bool _annule;

    public ChoiceListDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<ChoiceListDialog<string>> Liste() =>
        Render<ChoiceListDialog<string>>(p => p
            .Add(d => d.Title, "Unité (F4)")
            .Add(d => d.Items, Unites)
            .Add(d => d.Matches, (u, f) => u.Contains(f, StringComparison.CurrentCultureIgnoreCase))
            .Add(d => d.ItemText, u => u)
            .Add(d => d.OnSelected, u => _choisi = u)
            .Add(d => d.OnCancel, () => _annule = true));

    [Fact]
    public void Fleche_bas_puis_Entree_choisit_le_deuxieme_element()
    {
        IRenderedComponent<ChoiceListDialog<string>> liste = Liste();

        liste.Find("input").KeyDown("ArrowDown");
        liste.FindAll("li[aria-selected=true]").Single().TextContent.Should().Be("Jour");
        liste.Find("input").KeyDown("Enter");

        _choisi.Should().Be("Jour");
    }

    [Fact]
    public void La_saisie_filtre_et_Entree_prend_le_premier_resultat()
    {
        IRenderedComponent<ChoiceListDialog<string>> liste = Liste();

        liste.Find("input").Input("for");
        liste.FindAll("li").Select(l => l.TextContent).Should().Equal("Forfait");
        liste.Find(".lg-hint").TextContent.Should().Contain("1 résultat(s)");
        liste.Find("input").KeyDown("Enter");

        _choisi.Should().Be("Forfait");
    }

    [Fact]
    public void Echap_annule_et_le_focus_va_dans_la_saisie_a_l_ouverture()
    {
        IRenderedComponent<ChoiceListDialog<string>> liste = Liste();

        JSInterop.Invocations.Should().Contain(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));
        liste.Find("input").KeyDown("Escape");

        _annule.Should().BeTrue();
        _choisi.Should().BeNull();
    }

    [Fact]
    public void La_liste_est_accessible_et_traduite()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));
        IRenderedComponent<ChoiceListDialog<string>> liste = Liste();

        liste.Find("[role=dialog]").GetAttribute("aria-modal").Should().Be("true");
        string? actif = liste.Find("input").GetAttribute("aria-activedescendant");
        liste.Find($"#{actif}").TextContent.Should().Be("Heure");
        liste.Find(".lg-hint").TextContent.Should().Contain("Enter to confirm");
    }
}
