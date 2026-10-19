using System.Globalization;
using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Cabinet;
using Lettra.UI.Shared.Components.Cabinet;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Cabinet;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Cabinet;

/// <summary>
/// Vue cabinet (vague 6) : le sélecteur de dossier de la barre du haut, le tableau de supervision des dossiers et la
/// revue de dossier par période, comme un collaborateur les utilise.
/// </summary>
[Collection("culture")]
public sealed class CabinetScreensTests : BunitContext
{
    private readonly FakeCabinetGateway _gateway = new();
    private readonly SwitchableDossierSelection _selection = new(FakeCabinetGateway.Boulangerie);

    public CabinetScreensTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes(_gateway);
        Services.AddSingleton<IDossierSelection>(_selection);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void Le_selecteur_montre_le_dossier_ouvert_et_en_change_en_un_clic()
    {
        IRenderedComponent<DossierPicker> picker = Render<DossierPicker>();
        picker.WaitForAssertion(() => picker.Find("#dossier-picker").TextContent.Should().Contain("Boulangerie Martin"));

        picker.Find("#dossier-picker").Click();
        picker.FindAll(".lt-dossier-open").Select(b => b.TextContent.Trim()).Should().Equal("Atelier Éole", "Boulangerie Martin", "Garage du Centre");
        picker.Find(".lt-dossier-item.is-current").TextContent.Should().Contain("Boulangerie Martin");

        picker.FindAll(".lt-dossier-open").Single(b => b.TextContent.Contains("Garage", StringComparison.Ordinal)).Click();

        _selection.CurrentDossierId.Should().Be(FakeCabinetGateway.Garage);
        this.ToastText().Should().Be("Dossier « Garage du Centre » ouvert.");
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/");
        picker.Find("#dossier-picker").TextContent.Should().Contain("Garage du Centre");

        // Le dossier quitté est dans les récents ; une étoile le met en favori.
        picker.Find("#dossier-picker").Click();
        picker.Markup.Should().Contain("Récents");
        picker.FindAll(".lt-dossier-star")[0].Click();
        Services.GetRequiredService<CurrentDossier>().Favourites.Should().ContainSingle();
    }

    [Fact]
    public void Sur_un_hote_a_dossier_fixe_le_selecteur_ne_propose_pas_d_en_changer()
    {
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(FakeCabinetGateway.Boulangerie));
        IRenderedComponent<DossierPicker> picker = Render<DossierPicker>();
        picker.WaitForAssertion(() => picker.Find("#dossier-picker").TextContent.Should().Contain("Boulangerie Martin"));

        picker.Find("#dossier-picker").Click();

        picker.FindAll(".lt-dossier-open[disabled]").Should().HaveCount(2, "seul le dossier ouvert reste actif");
    }

    [Fact]
    public void Le_tableau_classe_les_dossiers_par_urgence_et_ajoute_les_colonnes_des_autres_modules()
    {
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();

        page.WaitForAssertion(() => page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3));
        page.FindAll(".lt-cabinet-table tbody th").Select(t => t.TextContent).Should()
            .SatisfyRespectively(
                g => g.Should().Contain("Garage du Centre"),
                a => a.Should().Contain("Atelier Éole"),
                b => b.Should().Contain("Boulangerie Martin"));
        page.FindAll(".lt-cabinet-table thead th").Select(t => t.TextContent.Trim()).Should().Contain("module.futur");
        page.FindAll(".lt-cabinet-table thead th").Select(t => t.TextContent.Trim()).Should().NotContain("Calculé", "l'heure du calcul est sous le nom du dossier");
        page.FindAll(".lt-cabinet-table tbody th .lt-cabinet-when").Should().HaveCount(3);
        page.Find(".lt-cabinet-kpis .lt-kpi-value").TextContent.Should().Be("2", "deux dossiers sont à traiter");
        _gateway.Refreshes.Should().Be(0, "les chiffres sont déjà calculés");

        page.Find("#cabinet-urgent").Change(true);
        page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(2);

        page.Find("#cabinet-search").Input("eole");
        page.FindAll(".lt-cabinet-table tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("Atelier Éole");
    }

    [Fact]
    public void Chaque_dossier_montre_son_niveau_et_ce_qui_demande_une_action()
    {
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3));

        var rows = page.FindAll(".lt-cabinet-table tbody tr");
        rows.Select(r => r.GetAttribute("data-level")).Should().Equal("Critical", "Warning", "Ok");
        rows.Select(r => r.QuerySelector(".lt-cabinet-avatar")!.TextContent).Should().Equal("GC", "AÉ", "BM");
        rows.Select(r => r.QuerySelector(".lt-level-badge")!.TextContent).Should().Equal("Urgent", "À traiter", "À jour");

        // Un compteur à zéro reste discret ; seul le dossier sans rien à faire dit « Tout est à jour » (en carte).
        rows[1].QuerySelector("td[data-label='Factures en retard'] .lt-indicator")!.ClassList.Should().Contain("is-zero");
        rows[1].QuerySelector("td[data-label='Factures en retard']")!.ClassList.Should().Contain("is-clear");
        rows[0].QuerySelector("td[data-label='Factures en retard'] .lt-indicator-due")!.TextContent.Should().StartWith("depuis le 31");
        page.FindAll(".lt-cabinet-clear").Should().ContainSingle().Which.TextContent.Should().Contain("Tout est à jour");
        rows[2].QuerySelector(".lt-cabinet-clear").Should().NotBeNull();

        page.FindAll(".lt-cabinet-kpis .lt-kpi.is-alert").Should().HaveCount(4, "dossiers à traiter, factures en retard, rejets et revue ont tous des chiffres");
        page.Find(".lt-cabinet-count").TextContent.Should().Be("3");
    }

    [Fact]
    public void Effacer_les_filtres_remet_tous_les_dossiers()
    {
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3));

        page.Find("#cabinet-search").Input("garage");
        page.Find("#cabinet-urgent").Change(true);
        page.FindAll(".lt-cabinet-table tbody tr").Should().ContainSingle();
        page.Find(".lt-cabinet-count").TextContent.Should().Be("1");

        page.Find("#cabinet-filters-reset").Click();

        page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3);
        page.Find("#cabinet-search").GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData("Bâti Rénov' Lemoine", "BR")]
    [InlineData("Dossier de démonstration", "D")]
    [InlineData("Négoce Méditerranée Distribution", "NM")]
    [InlineData("atelier", "A")]
    public void Les_initiales_viennent_des_mots_en_majuscule(string name, string initials) =>
        CabinetPage.Initials(name).Should().Be(initials);

    [Fact]
    public void Un_tableau_jamais_calcule_se_calcule_a_l_ouverture_et_actualiser_le_dit_dans_le_toaster()
    {
        _gateway.Computed = false;
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();

        page.WaitForAssertion(() => _gateway.Refreshes.Should().Be(1));
        page.WaitForAssertion(() => page.Markup.Should().Contain("module.futur"));
        this.Toasts().Should().BeEmpty("le calcul automatique ne fait pas de bruit");

        page.Find("#cabinet-refresh").Click();
        page.WaitForAssertion(() => this.ToastText().Should().Be("Chiffres recalculés pour 3 dossier(s)."));
    }

    [Fact]
    public void Revoir_un_dossier_depuis_le_tableau_ouvre_ce_dossier_sur_sa_revue()
    {
        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();
        page.WaitForAssertion(() => page.FindAll(".lt-cabinet-table tbody tr").Should().HaveCount(3));

        page.FindAll(".lt-cabinet-table tbody tr")[1].QuerySelectorAll(".lt-cabinet-actions button")[^1].Click();

        _selection.CurrentDossierId.Should().Be(FakeCabinetGateway.Atelier);
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/review");
    }

    [Fact]
    public void La_revue_se_coche_avec_un_message_et_garde_qui_et_quand()
    {
        IRenderedComponent<DossierReviewPage> page = Render<DossierReviewPage>(p => p.Add(r => r.Period, "2026-09"));
        page.WaitForAssertion(() => page.FindAll(".lt-review-item").Should().HaveCount(8));
        page.Find(".lt-review-progress-text").TextContent.Should().Be("0 point(s) sur 8");

        page.Find("#review-banque-rapprochee").Change(true);

        page.WaitForAssertion(() => page.Find(".lt-review-progress-text").TextContent.Should().Be("1 point(s) sur 8"));
        _gateway.Sets.Should().ContainSingle().Which.Should().Match<(string Period, string Item, Contracts.Cabinet.SetDossierReviewItemRequest Request)>(
            s => s.Period == "2026-09" && s.Item == "banque.rapprochee" && s.Request.Checked);
        this.ToastText().Should().StartWith("Point coché");
        page.Find(".lt-review-item.is-done .lt-review-who").TextContent.Should().StartWith("Coché par vous");

        page.Find("#review-banque-rapprochee").Change(false);
        page.WaitForAssertion(() => page.Find(".lt-review-progress-text").TextContent.Should().Be("0 point(s) sur 8"));
        this.ToastText().Should().StartWith("Point décoché");
    }
}
