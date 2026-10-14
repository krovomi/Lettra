using Bunit;
using Lettra.Poc.Grille.Calculs;
using Lettra.Poc.Grille.Ui;
using Lettra.Poc.Grille.Ui.Clavier;
using Lettra.Poc.Grille.Ui.Grille;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Poc.Grille.Tests;

public class DataEntryGridTests : BunitContext
{
    private static readonly IReadOnlyList<GridColumn<LigneFacture>> Colonnes =
    [
        new() { Key = "article", Label = "Article", Display = l => l.Article, Parse = (l, t) => l with { Article = t } },
        new() { Key = "quantite", Label = "Qté", Numeric = true, Display = l => l.Quantite.ToString("0.##"),
                Parse = (l, t) => l with { Quantite = decimal.Parse(t) } },
        new() { Key = "ht", Label = "Montant HT", Numeric = true, Display = l => l.MontantHt.ToString("0.00") },
    ];

    private readonly List<LigneFacture> _lignes =
        Enumerable.Range(1, 1_000).Select(i => LigneFacture.Vide() with { Article = $"A{i}", PrixUnitaireHt = 10m }).ToList();

    public DataEntryGridTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraGrilleClavier();
    }

    private IRenderedComponent<DataEntryGrid<LigneFacture>> Grille(Action<ComponentParameterCollectionBuilder<DataEntryGrid<LigneFacture>>>? plus = null) =>
        Render<DataEntryGrid<LigneFacture>>(p =>
        {
            p.Add(g => g.Items, _lignes).Add(g => g.Columns, Colonnes).Add(g => g.KeySelector, l => l.Id);
            plus?.Invoke(p);
        });

    [Fact]
    public void Les_lignes_ne_sont_pas_toutes_rendues_et_les_colonnes_calculees_ne_sont_pas_saisissables()
    {
        var grille = Grille();
        var lignes = grille.FindAll(".lg-row[data-r]");
        Assert.InRange(lignes.Count, 1, 999);         // virtualisation : jamais les 1 000 lignes
        Assert.Equal(2, lignes[0].QuerySelectorAll("input.lg-input").Length);
        Assert.Equal("1001", grille.Find("[role=grid]").GetAttribute("aria-rowcount"));
    }

    [Fact]
    public void Le_module_JS_recoit_les_colonnes_saisissables_et_les_seules_touches_de_commande()
    {
        var scope = new KeyboardScope("grille").Bind("F4", "Liste", _ => { }).Bind("Ctrl+Delete", "Supprimer", _ => { });
        Grille(p => p.Add(g => g.Scope, scope));

        var attache = JSInterop.Invocations.Single(i => i.Identifier == "attachGrid");
        var options = attache.Arguments[2]!;
        Assert.Equal(new[] { 0, 1 }, (int[])options.GetType().GetProperty("editableColumns")!.GetValue(options)!);
        Assert.Equal(new[] { "F4", "Ctrl+Delete" }, (string[])options.GetType().GetProperty("commandKeys")!.GetValue(options)!);
    }

    [Fact]
    public async Task Une_saisie_valide_remplace_la_ligne_et_notifie_avant_apres()
    {
        GridItemChange<LigneFacture>? changement = null;
        var grille = Grille(p => p.Add(g => g.OnItemChanged, c => changement = c));

        var resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(2, 1, "4"));

        Assert.Equal(new CellCommitResult("4", true), resultat);
        Assert.Equal(4m, _lignes[2].Quantite);
        Assert.Equal(1m, changement!.Value.Before.Quantite);
        Assert.Equal("40.00", grille.Find(".lg-row[data-r='2'] .lg-calc").TextContent);
    }

    [Fact]
    public async Task Une_saisie_invalide_est_refusee_et_l_ancien_texte_renvoye()
    {
        var grille = Grille();
        var resultat = await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(0, 1, "abc"));
        Assert.Equal(new CellCommitResult("1", false), resultat);
        Assert.Equal(1m, _lignes[0].Quantite);
    }

    [Fact]
    public async Task Seule_la_ligne_modifiee_est_rendue_a_nouveau()
    {
        var grille = Grille();
        var avant = GridRow<LigneFacture>.RenderCount;
        await grille.InvokeAsync(() => grille.Instance.OnCellCommitted(3, 0, "NOUVEAU"));
        Assert.Equal(1, GridRow<LigneFacture>.RenderCount - avant);
    }

    [Fact]
    public async Task Une_commande_passe_par_le_registre_avec_la_cellule_active_et_peut_deplacer_le_curseur()
    {
        CommandContext? recu = null;
        DataEntryGrid<LigneFacture>? instance = null;
        var scope = new KeyboardScope("grille").Bind("Insert", "Insérer", async ctx =>
        {
            recu = ctx;
            await instance!.FocusCellAsync(ctx.Row + 1, "article");
        });
        var grille = Grille(p => p.Add(g => g.Scope, scope));
        instance = grille.Instance;

        var cible = await grille.InvokeAsync(() => grille.Instance.OnCommand("Insert", 5, 1));

        Assert.Equal(new CommandContext(5, "quantite"), recu);
        Assert.Equal(new FocusTarget(6, 0), cible);
    }

    [Fact]
    public async Task Fin_de_grille_la_page_ajoute_une_ligne_et_le_curseur_va_sur_la_premiere_colonne()
    {
        var grille = Grille(p => p.Add(g => g.EndReached, () => _lignes.Add(LigneFacture.Vide())));
        var cible = await grille.InvokeAsync(() => grille.Instance.OnEndReached(1));
        Assert.Equal(1_001, _lignes.Count);
        Assert.Equal(new FocusTarget(1_000, 0), cible);
    }
}
