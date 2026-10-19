using System.Globalization;
using Bunit;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

[Collection("culture")]
public sealed class CatalogPageTests : BunitContext
{
    private const int Reference = 0, Designation = 1, Unite = 3, PrixHt = 4;

    private readonly FakeCatalogGateway _gateway = new();

    public CatalogPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<ICatalogGateway>(_gateway);
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private static string Fr(decimal value) => GridNumber.FormatPrice(value, CultureInfo.GetCultureInfo("fr-FR"));

    private static string Cellule(IRenderedComponent<Catalog> page, int row, int col) =>
        page.Find($"input.lg-input[data-r='{row}'][data-c='{col}']").GetAttribute("value") ?? string.Empty;

    private static IRenderedComponent<DataEntryGrid<CatalogRow>> Grille(IRenderedComponent<Catalog> page) =>
        page.FindComponent<DataEntryGrid<CatalogRow>>();

    private static Task<CellCommitResult> Saisir(IRenderedComponent<Catalog> page, int row, int col, string text)
    {
        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);
        return page.InvokeAsync(() => grille.Instance.OnCellCommitted(row, col, text));
    }

    [Fact]
    public void Les_entetes_sont_en_francais()
    {
        IRenderedComponent<Catalog> page = Render<Catalog>();

        page.Find("h1").TextContent.Should().Be("Catalogue");
        page.FindAll(".lg-head").Select(h => h.TextContent)
            .Should().Equal("Référence", "Désignation", "Nature", "Unité", "PU HT", "TVA", "PU TTC");
    }

    [Fact]
    public void La_grille_propose_de_revenir_a_la_liste()
    {
        IRenderedComponent<Catalog> page = Render<Catalog>();

        page.Find("#catalog-view-grid").GetAttribute("aria-current").Should().Be("page");
        page.Find("#catalog-view-list").HasAttribute("aria-current").Should().BeFalse();
        page.Find("#catalog-view-list").GetAttribute("href").Should().Be("catalog");
        page.Find("#catalog-view-list").TextContent.Trim().Should().Be("Liste");
    }

    [Fact]
    public void Les_entetes_sont_traduits_en_anglais()
    {
        UseCulture("en-US");

        IRenderedComponent<Catalog> page = Render<Catalog>();

        page.FindAll(".lg-head").Select(h => h.TextContent)
            .Should().Equal("Reference", "Description", "Type", "Unit", "Unit price excl. VAT", "VAT", "Unit price incl. VAT");
        page.Find("label[for=catalog-archived]").TextContent.Trim().Should().Be("Show archived items");
        page.Find("#catalog-view").GetAttribute("aria-label").Should().Be("View");
        page.Find("#catalog-view-grid").TextContent.Trim().Should().Be("Grid entry");
    }

    [Fact]
    public void Le_chargement_affiche_les_articles_avec_le_prix_TTC_calcule()
    {
        _gateway.Add("DEV-01", "Développement", 1000m);
        _gateway.Add("FORM", "Formation", 99.99m, "FR-5.5", "DAY");

        IRenderedComponent<Catalog> page = Render<Catalog>();

        Cellule(page, 0, Reference).Should().Be("DEV-01");
        Cellule(page, 0, 2).Should().Be("Service");
        Cellule(page, 0, Unite).Should().Be("Heure");
        Cellule(page, 0, PrixHt).Should().Be(Fr(1000m));
        Cellule(page, 0, 5).Should().Be("20 %");
        page.Find(".lg-row[data-r='0'] .lg-calc").TextContent.Should().Be(Fr(1200m));
        Cellule(page, 1, Unite).Should().Be("Jour");
        Cellule(page, 1, 5).Should().Be("5,5 %");
        page.Find(".lg-row[data-r='1'] .lg-calc").TextContent.Should().Be("105,49");
        _gateway.ListCalls.Should().ContainSingle().Which.Should().Be((null, false, 500));
    }

    [Fact]
    public void La_grille_confie_au_module_JS_les_colonnes_saisissables_et_les_touches_de_commande()
    {
        Render<Catalog>();

        GridKeyboardOptions options = (GridKeyboardOptions)JSInterop.Invocations.Single(i => i.Identifier == "attachGrid").Arguments[2]!;
        options.EditableColumns.Should().Equal(0, 1, 2, 3, 4, 5);
        options.CommandKeys.Should().BeEquivalentTo("F4", "Insert", "Ctrl+Delete");
        JSInterop.Invocations.Single(i => i.Identifier == "attachGlobal").Arguments[1]
            .Should().BeEquivalentTo(new[] { "F3" });   // Ctrl+Shift+D (densité) est désormais global : MainLayout
    }

    [Fact]
    public async Task Modifier_un_prix_enregistre_l_article_complet_avec_le_decimal_lu_en_francais()
    {
        CatalogItemResponse item = _gateway.Add("DEV-01", "Développement", 1000m);
        IRenderedComponent<Catalog> page = Render<Catalog>();

        CellCommitResult resultat = await Saisir(page, 0, PrixHt, "1 234,50");

        resultat.Should().Be(new CellCommitResult(Fr(1234.50m), true));
        (Guid id, SaveCatalogItemRequest requete) = _gateway.Updates.Should().ContainSingle().Subject;
        id.Should().Be(item.Id);
        requete.UnitPriceExclTax.Should().Be(1234.50m);
        requete.Reference.Should().Be("DEV-01");
        requete.Designation.Should().Be("Développement");
        requete.Description.Should().Be("Descriptif DEV-01", "la description, absente de la grille, ne doit pas être effacée");
        requete.UnitCode.Should().Be("HUR");
        requete.VatRateCode.Should().Be("FR-20");
        page.WaitForAssertion(() => page.Find(".lg-row[data-r='0'] .lg-calc").TextContent.Should().Be(Fr(1481.40m)));
        this.ToastText().Should().Be("Article « DEV-01 » enregistré.");
    }

    [Fact]
    public async Task Une_erreur_du_serveur_s_affiche_traduite_et_la_valeur_revient()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        _gateway.NextErrors = [new ErrorDetail(ErrorCodes.CatalogItem.PriceNegative, "UnitPriceExclTax")];
        IRenderedComponent<Catalog> page = Render<Catalog>();

        CellCommitResult resultat = await Saisir(page, 0, PrixHt, "-5");

        resultat.Should().Be(new CellCommitResult(Fr(100m), false));
        page.WaitForAssertion(() => this.ToastText().Should().Be("Le prix ne peut pas être négatif."));
        Cellule(page, 0, PrixHt).Should().Be(Fr(100m));
        _gateway.Items[0].UnitPriceExclTax.Should().Be(100m);
    }

    [Fact]
    public async Task Un_collegue_a_modifie_un_autre_champ_la_saisie_est_fusionnee_et_enregistree()
    {
        CatalogItemResponse item = _gateway.Add("DEV-01", "Développement", 1000m);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        _gateway.EditedByColleague(item.Id, i => i with { Designation = "Développement spécifique" });

        CellCommitResult resultat = await Saisir(page, 0, PrixHt, "1200");

        resultat.Ok.Should().BeTrue();
        _gateway.Items[0].UnitPriceExclTax.Should().Be(1200m, "ma saisie est gardée");
        _gateway.Items[0].Designation.Should().Be("Développement spécifique", "celle du collègue aussi");
        _gateway.Items[0].Version.Should().Be(3);
        page.WaitForAssertion(() => Cellule(page, 0, Designation).Should().Be("Développement spécifique"));
        this.ToastText().Should().Contain("fusionnés");
    }

    [Fact]
    public async Task Un_collegue_a_modifie_le_meme_champ_sa_valeur_s_affiche_et_rien_n_est_ecrase()
    {
        CatalogItemResponse item = _gateway.Add("DEV-01", "Développement", 1000m);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        _gateway.EditedByColleague(item.Id, i => i with { UnitPriceExclTax = 1100m });

        await Saisir(page, 0, PrixHt, "1200");

        _gateway.Items[0].UnitPriceExclTax.Should().Be(1100m);
        _gateway.Updates.Should().ContainSingle("pas de nouvel envoi quand le même champ est en conflit");
        page.WaitForAssertion(() => this.ToastText().Should().Contain("modifié par quelqu'un d'autre"));
        page.WaitForAssertion(() => Cellule(page, 0, PrixHt).Should().Be(Fr(1100m)));
    }

    [Fact]
    public async Task Une_saisie_illisible_est_refusee_sans_appeler_le_serveur()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        IRenderedComponent<Catalog> page = Render<Catalog>();

        (await Saisir(page, 0, PrixHt, "douze")).Should().Be(new CellCommitResult(Fr(100m), false));
        (await Saisir(page, 0, Unite, "zzz")).Ok.Should().BeFalse();

        _gateway.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_unite_se_saisit_par_son_libelle_ou_son_code()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        IRenderedComponent<Catalog> page = Render<Catalog>();

        (await Saisir(page, 0, Unite, "forf")).Should().Be(new CellCommitResult("Forfait", true));
        (await Saisir(page, 0, Unite, "day")).Should().Be(new CellCommitResult("Jour", true));

        _gateway.Updates.Select(u => u.Request.UnitCode).Should().Equal("LS", "DAY");
    }

    [Fact]
    public async Task Une_nouvelle_ligne_en_fin_de_grille_est_creee_quand_reference_et_designation_sont_saisies()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);

        FocusTarget? cible = await page.InvokeAsync(() => grille.Instance.OnEndReached(5));
        cible.Should().Be(new FocusTarget(1, Reference));
        await Saisir(page, 1, Reference, "AUDIT");
        _gateway.Creations.Should().BeEmpty();
        await Saisir(page, 1, Designation, "Audit annuel");
        await Saisir(page, 1, PrixHt, "450");

        _gateway.Creations.Should().ContainSingle().Which.Should().Match<SaveCatalogItemRequest>(r =>
            r.Reference == "AUDIT" && r.Designation == "Audit annuel" && r.UnitCode == "C62" && r.VatRateCode == "FR-20");
        _gateway.Updates.Should().ContainSingle().Which.Request.UnitPriceExclTax.Should().Be(450m);
        page.WaitForAssertion(() => page.Find(".lg-row[data-r='1'] .lg-calc").TextContent.Should().Be("540,00"));
    }

    [Fact]
    public async Task Ctrl_Suppr_archive_et_masque_la_ligne()
    {
        CatalogItemResponse item = _gateway.Add("DEV-01", "Développement", 100m);
        _gateway.Add("FORM", "Formation", 50m);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);

        await page.InvokeAsync(() => grille.Instance.OnCommand("Ctrl+Delete", 0, Reference));

        _gateway.ArchiveCalls.Should().Equal((item.Id, true));
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        Cellule(page, 0, Reference).Should().Be("FORM");
        this.ToastText().Should().Be("Article « DEV-01 » archivé.");
    }

    [Fact]
    public async Task Les_articles_archives_s_affichent_sur_demande_en_lecture_seule_et_se_reactivent()
    {
        CatalogItemResponse item = _gateway.Add("VIEUX", "Ancien article", 10m, archived: true);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        page.FindAll(".lg-row[data-r]").Should().BeEmpty();

        page.Find("#catalog-archived").Change(true);

        page.WaitForAssertion(() => page.FindAll(".lg-row.lg-archived").Should().HaveCount(1));
        _gateway.ListCalls.Last().IncludeArchived.Should().BeTrue();
        page.Find("input.lg-input[data-r='0'][data-c='0']").HasAttribute("readonly").Should().BeTrue();
        (await Saisir(page, 0, PrixHt, "20")).Ok.Should().BeFalse();

        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);
        await page.InvokeAsync(() => grille.Instance.OnCommand("Ctrl+Delete", 0, Reference));

        _gateway.ArchiveCalls.Should().Equal((item.Id, false));
        page.WaitForAssertion(() => page.FindAll(".lg-row.lg-archived").Should().BeEmpty());
        page.Find("input.lg-input[data-r='0'][data-c='0']").HasAttribute("readonly").Should().BeFalse();
    }

    [Fact]
    public async Task F4_sur_l_unite_ouvre_la_liste_de_choix_et_fleche_bas_Entree_choisit()
    {
        _gateway.Add("DEV-01", "Développement", 100m, unit: "C62");
        IRenderedComponent<Catalog> page = Render<Catalog>();
        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);

        await page.InvokeAsync(() => grille.Instance.OnCommand("F4", 0, Unite));

        page.Find("[role=dialog] h2").TextContent.Should().Be("Unité (F4)");
        page.Find("[role=dialog] input").KeyDown("ArrowDown");
        page.Find("[role=dialog] input").KeyDown("Enter");

        page.WaitForAssertion(() => page.FindAll("[role=dialog]").Should().BeEmpty());
        _gateway.Updates.Should().ContainSingle().Which.Request.UnitCode.Should().Be("HUR");
        Cellule(page, 0, Unite).Should().Be("Heure");
    }

    [Fact]
    public async Task F4_hors_des_colonnes_a_choix_ne_fait_rien()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        IRenderedComponent<Catalog> page = Render<Catalog>();
        IRenderedComponent<DataEntryGrid<CatalogRow>> grille = Grille(page);

        await page.InvokeAsync(() => grille.Instance.OnCommand("F4", 0, PrixHt));

        page.FindAll("[role=dialog]").Should().BeEmpty();
    }

    [Fact]
    public void La_recherche_interroge_le_serveur()
    {
        _gateway.Add("DEV-01", "Développement", 100m);
        _gateway.Add("FORM", "Formation", 50m);
        IRenderedComponent<Catalog> page = Render<Catalog>();

        page.Find("#catalog-search").Change("form");

        _gateway.ListCalls.Last().Search.Should().Be("form");
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        Cellule(page, 0, Reference).Should().Be("FORM");
    }

    [Fact]
    public void Le_bouton_nouvel_article_ajoute_une_ligne_vierge()
    {
        IRenderedComponent<Catalog> page = Render<Catalog>();
        page.Markup.Should().Contain("Aucun article pour l'instant.");

        page.Find("#catalog-new").Click();

        page.FindAll(".lg-row[data-r]").Should().HaveCount(1);
        Cellule(page, 0, Unite).Should().Be("Unité");
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "focusCell");
    }
}
