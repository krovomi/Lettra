using System.Globalization;
using Bunit;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Localization;

namespace Lettra.UI.Tests;

public sealed record Ligne(int Id, string Article, decimal Quantite, decimal Prix)
{
    public decimal Montant => Quantite * Prix;
}

[Collection("culture")]
public sealed class DataEntryGridTests : BunitContext
{
    private static readonly IReadOnlyList<GridColumn<Ligne>> Colonnes =
    [
        new() { Key = "article", Label = "Article", Display = l => l.Article, Parse = (l, t) => l with { Article = t } },
        new() { Key = "quantite", Label = "Qté", Numeric = true, Display = l => l.Quantite.ToString("0.##", CultureInfo.InvariantCulture),
                Parse = (l, t) => l with { Quantite = decimal.Parse(t, CultureInfo.InvariantCulture) } },
        new() { Key = "montant", Label = "Montant", Numeric = true, Display = l => l.Montant.ToString("0.00", CultureInfo.InvariantCulture) },
    ];

    private readonly List<Ligne> _lignes = Enumerable.Range(1, 1_000).Select(i => new Ligne(i, $"A{i}", 1m, 10m)).ToList();

    public DataEntryGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<DataEntryGrid<Ligne>> Grille(Action<ComponentParameterCollectionBuilder<DataEntryGrid<Ligne>>>? plus = null) =>
        Render<DataEntryGrid<Ligne>>(p =>
        {
            p.Add(g => g.Items, _lignes).Add(g => g.Columns, Colonnes).Add(g => g.KeySelector, l => l.Id);
            plus?.Invoke(p);
        });

    [Fact]
    public void Les_lignes_ne_sont_pas_toutes_rendues_et_les_colonnes_calculees_ne_sont_pas_saisissables()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille();

        IReadOnlyList<AngleSharp.Dom.IElement> lignes = grille.FindAll(".lg-row[data-r]");
        lignes.Count.Should().BeInRange(1, 999);
        lignes[0].QuerySelectorAll("input.lg-input").Length.Should().Be(2);
        grille.Find("[role=grid]").GetAttribute("aria-rowcount").Should().Be("1001");
    }

    [Fact]
    public void Les_cellules_ont_un_libelle_accessible_traduit()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille();

        grille.Find("input[data-r='0'][data-c='1']").GetAttribute("aria-label").Should().Be("Qté, ligne 1");
        grille.Find("[role=grid]").GetAttribute("aria-label").Should().Be("Grille de saisie");
    }

    [Fact]
    public void Le_module_JS_est_attache_avec_les_colonnes_saisissables_et_les_seules_touches_de_commande()
    {
        KeyboardScope scope = new KeyboardScope("grille").Bind("F4", "Liste", _ => { }).Bind("Ctrl+Delete", "Supprimer", _ => { });
        Grille(p => p.Add(g => g.Scope, scope));

        JSInterop.Invocations.Should().Contain(i => i.Identifier == "import" && (string?)i.Arguments[0] == GridKeyboardModule.Path);
        GridKeyboardOptions options = (GridKeyboardOptions)JSInterop.Invocations.Single(i => i.Identifier == "attachGrid").Arguments[2]!;
        options.EditableColumns.Should().Equal(0, 1);
        options.CommandKeys.Should().Equal("F4", "Ctrl+Delete");
    }

    [Fact]
    public async Task Une_saisie_valide_remplace_la_ligne_et_notifie_avant_apres()
    {
        GridItemChange<Ligne>? changement = null;
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille(p => p.Add(g => g.OnItemChanged, c => changement = c));

        CellCommitResult resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(2, 1, "4"));

        resultat.Should().Be(new CellCommitResult("4", true));
        _lignes[2].Quantite.Should().Be(4m);
        changement!.Value.Before.Quantite.Should().Be(1m);
        grille.Find(".lg-row[data-r='2'] .lg-calc").TextContent.Should().Be("40.00");
    }

    [Fact]
    public async Task Une_saisie_invalide_est_refusee_et_l_ancien_texte_renvoye()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille();

        CellCommitResult resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(0, 1, "abc"));

        resultat.Should().Be(new CellCommitResult("1", false));
        _lignes[0].Quantite.Should().Be(1m);
    }

    [Fact]
    public async Task Une_saisie_refusee_par_l_enregistrement_garde_l_ancienne_valeur()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille(p => p.Add(g => g.SaveItem, _ => Task.FromResult<Ligne?>(null)));

        CellCommitResult resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(0, 1, "7"));

        resultat.Should().Be(new CellCommitResult("1", false));
        _lignes[0].Quantite.Should().Be(1m);
    }

    [Fact]
    public async Task L_element_renvoye_par_l_enregistrement_est_celui_affiche()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille(p => p.Add(g => g.SaveItem, c => Task.FromResult<Ligne?>(c.After with { Article = c.After.Article.ToUpperInvariant() })));

        CellCommitResult resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(0, 0, "abc"));

        resultat.Should().Be(new CellCommitResult("ABC", true));
        _lignes[0].Article.Should().Be("ABC");
    }

    [Fact]
    public async Task Seule_la_ligne_modifiee_est_rendue_a_nouveau()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille();
        Dictionary<int, int> avant = grille.FindComponents<GridRow<Ligne>>().ToDictionary(r => r.Instance.Index, r => r.RenderCount);

        await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(3, 0, "NOUVEAU"));

        grille.FindComponents<GridRow<Ligne>>()
            .Where(r => avant.ContainsKey(r.Instance.Index) && r.RenderCount != avant[r.Instance.Index])
            .Select(r => r.Instance.Index)
            .Should().Equal(3);
    }

    [Fact]
    public async Task Une_commande_passe_par_le_registre_avec_la_cellule_active_et_peut_deplacer_le_curseur()
    {
        CommandContext? recu = null;
        DataEntryGrid<Ligne>? instance = null;
        KeyboardScope scope = new KeyboardScope("grille").Bind("Insert", "Insérer", async ctx =>
        {
            recu = ctx;
            await instance!.FocusCellAsync(ctx.Row + 1, "article");
        });
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille(p => p.Add(g => g.Scope, scope));
        instance = grille.Instance;

        FocusTarget? cible = await grille.InvokeAsync(() => grille.Instance.OnCommand("Insert", 5, 1));

        recu.Should().Be(new CommandContext(5, "quantite"));
        cible.Should().Be(new FocusTarget(6, 0));
    }

    [Fact]
    public async Task Fin_de_grille_la_page_ajoute_une_ligne_et_le_curseur_va_sur_la_premiere_colonne()
    {
        IRenderedComponent<DataEntryGrid<Ligne>> grille = Grille(p => p.Add(g => g.EndReached, () => _lignes.Add(new Ligne(1_001, string.Empty, 0m, 0m))));

        FocusTarget? cible = await grille.InvokeAsync(() => grille.Instance.OnEndReached(1));

        _lignes.Should().HaveCount(1_001);
        cible.Should().Be(new FocusTarget(1_000, 0));
    }

    [Fact]
    public void La_barre_de_touches_affiche_les_commandes_de_la_grille_et_les_execute_au_clic()
    {
        List<string> appels = [];
        KeyboardScope scope = new KeyboardScope("grille").Bind("F4", "Liste de choix", _ => appels.Add("F4"));
        Grille(p => p.Add(g => g.Scope, scope));
        IRenderedComponent<FunctionKeyBar> barre = Render<FunctionKeyBar>();

        barre.Find("nav").GetAttribute("aria-label").Should().Be("Touches de fonction");
        barre.Find("button.lg-fkey").TextContent.Should().Be("F4Liste de choix");
        barre.Find("button.lg-fkey").Click();

        appels.Should().Equal("F4");
    }
}

public sealed class GridNumberTests
{
    [Theory]
    [InlineData("1 234,50", "fr-FR", 1234.50)]
    [InlineData("1 234,5", "fr-FR", 1234.5)]
    [InlineData("12.5", "fr-FR", 12.5)]
    [InlineData("99,99 €", "fr-FR", 99.99)]
    [InlineData("-3", "fr-FR", -3)]
    [InlineData("1,234.50", "en-US", 1234.50)]
    public void Les_nombres_saisis_sont_lus_dans_la_culture(string saisie, string culture, double attendu)
    {
        GridNumber.TryParse(saisie, CultureInfo.GetCultureInfo(culture), out decimal valeur).Should().BeTrue();
        valeur.Should().Be((decimal)attendu);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    public void Une_saisie_non_numerique_est_refusee(string saisie) =>
        GridNumber.TryParse(saisie, CultureInfo.GetCultureInfo("fr-FR"), out _).Should().BeFalse();

    [Fact]
    public void Les_prix_s_affichent_avec_separateur_de_milliers_et_deux_a_quatre_decimales()
    {
        CultureInfo fr = CultureInfo.GetCultureInfo("fr-FR");
        GridNumber.FormatPrice(1234.5m, fr).Should().Be($"1{fr.NumberFormat.NumberGroupSeparator}234,50");
        GridNumber.FormatPrice(0.1234m, fr).Should().Be("0,1234");
        GridNumber.FormatAmount(12m, CultureInfo.GetCultureInfo("en-US")).Should().Be("12.00");
    }
}
