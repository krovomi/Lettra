using System.Globalization;
using Bunit;
using Lettra.Contracts.Catalog;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.CatalogItems;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Consultation;

/// <summary>Page de consultation des articles : liste, fiche en lecture, saisie dans la fiche, archivage.</summary>
[Collection("culture")]
public sealed class CatalogExplorerTests : BunitContext
{
    private readonly FakeCatalogGateway _gateway = new();

    public CatalogExplorerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ExplorerSettings.SearchDelay = TimeSpan.FromMilliseconds(1);
        Services.AddLettraUi();
        Services.AddSingleton<ICatalogGateway>(_gateway);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<CatalogPage> Open(string route)
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<CatalogPage> page = Render<CatalogPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void La_fiche_montre_les_prix_HT_et_TTC_et_la_grille_reste_accessible()
    {
        CatalogItemResponse item = _gateway.Add("TENUE", "Tenue comptable", 450m);

        IRenderedComponent<CatalogPage> page = Open($"catalog/{item.Id}");

        page.Find("#catalog-sheet-title").TextContent.Should().Contain("Tenue comptable");
        page.Find("#catalog-kpi-excl").TextContent.Should().Contain(ExplorerText.Money(450m));
        page.Find("#catalog-kpi-incl").TextContent.Should().Contain(ExplorerText.Money(540m));
        page.Find("#catalog-view-grid").GetAttribute("href").Should().Be("catalog/grid");
    }

    [Fact]
    public void La_bascule_marque_la_liste_et_mene_a_la_grille()
    {
        IRenderedComponent<CatalogPage> page = Open("catalog");

        page.Find("#catalog-view-list").GetAttribute("aria-current").Should().Be("page");
        page.Find("#catalog-view-grid").HasAttribute("aria-current").Should().BeFalse();
        page.Find("#catalog-view-grid").TextContent.Trim().Should().Be("Saisie en grille");
    }

    [Fact]
    public void Creer_un_article_calcule_le_TTC_pendant_la_frappe_puis_ouvre_sa_fiche()
    {
        IRenderedComponent<CatalogPage> page = Open("catalog/new");

        page.Find("#item-reference").Change("CONSEIL");
        page.Find("#item-designation").Change("Conseil (heure)");
        page.Find("#item-price").Change("100");
        page.Find("#item-vat").Change("FR-10");
        page.Find("#item-price-incl").TextContent.Should().Be(ExplorerText.Money(110m));
        page.Find("#explorer-dirty");
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Article « Conseil (heure) » créé."));
        Guid id = _gateway.Items.Single().Id;
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"catalog/{id}");
        _gateway.Creations.Single().VatRateCode.Should().Be("FR-10");
    }

    [Fact]
    public void Modifier_un_article_envoie_sa_version()
    {
        CatalogItemResponse item = _gateway.Add("TENUE", "Tenue comptable", 450m);
        IRenderedComponent<CatalogPage> page = Open($"catalog/{item.Id}/edit");

        page.Find("#item-price").Change("480");
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Article « Tenue comptable » enregistré."));
        _gateway.Updates.Single().Request.UnitPriceExclTax.Should().Be(480m);
        page.Find("#catalog-kpi-excl").TextContent.Should().Contain(ExplorerText.Money(480m));
    }

    [Fact]
    public void Archiver_retire_l_article_de_la_liste_et_la_case_le_fait_revenir()
    {
        CatalogItemResponse item = _gateway.Add("ANCIEN", "Ancienne prestation", 80m);
        IRenderedComponent<CatalogPage> page = Open($"catalog/{item.Id}");

        page.Find("#catalog-archive").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Article « Ancienne prestation » archivé."));
        page.FindAll("#catalog-edit").Should().BeEmpty("un article archivé ne se modifie pas");
        page.FindAll("#catalog-items-list [role=option]").Should().BeEmpty();

        page.Find("#catalog-items-archived").Change(true);
        page.WaitForAssertion(() => page.FindAll("#catalog-items-list .lt-record.is-archived").Should().ContainSingle());
    }

    [Fact]
    public void Le_filtre_par_nature_ne_garde_que_les_biens_ou_les_services()
    {
        _gateway.Add("TENUE", "Tenue comptable", 450m);
        IRenderedComponent<CatalogPage> page = Open("catalog");
        page.FindAll("#catalog-items-list [role=option]").Should().HaveCount(1);

        page.Find("#catalog-kind-good").Click();

        page.FindAll("#catalog-items-list [role=option]").Should().BeEmpty();
        page.Find("#catalog-kind-good").GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void La_recherche_relance_la_liste_cote_serveur()
    {
        _gateway.Add("TENUE", "Tenue comptable", 450m);
        _gateway.Add("PAIE", "Bulletin de paie", 25m);
        IRenderedComponent<CatalogPage> page = Open("catalog");

        page.Find("#catalog-items-search").Input("paie");

        page.WaitForAssertion(() => page.FindAll("#catalog-items-list [role=option]").Should().ContainSingle().Which.TextContent.Should().Contain("Bulletin de paie"));
        _gateway.ListCalls.Last().Search.Should().Be("paie");
    }
}
