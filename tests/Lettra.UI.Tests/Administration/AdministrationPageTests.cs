using System.Globalization;
using Bunit;
using Lettra.Contracts.Administration;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using AdminPage = Lettra.UI.Shared.Pages.Administration.Administration;

namespace Lettra.UI.Tests.Administration;

/// <summary>
/// L'espace « Équipe et accès » tel qu'un administrateur non technicien le parcourt : des personnes nommées, ce qu'elles
/// peuvent faire écrit en clair, des gestes en trois questions qui se défont depuis le message de confirmation.
/// </summary>
[Collection("culture")]
public sealed class AdministrationPageTests : BunitContext
{
    private readonly FakeAdministrationGateway _gateway = new();

    public AdministrationPageTests()
    {
        Services.AddLettraUi();
        Services.AddSingleton<IAdministrationGateway>(_gateway);
        Services.AddSingleton<IDossierSelection>(new FixedDossierSelection(FakeAdministrationGateway.Boulangerie));
        JSInterop.Mode = JSRuntimeMode.Loose;
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void La_page_presente_l_equipe_en_chiffres_et_en_cartes()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("h1").TextContent.Should().Be("Équipe et accès");
        page.Find(".lt-page-header p").TextContent.Should().Contain("Cabinet Démo");
        page.Find("#kpi-active .lt-kpi-value").TextContent.Should().Be("4");
        page.Find("#kpi-pending .lt-kpi-value").TextContent.Should().Be("1");
        page.Find("#kpi-temporary .lt-kpi-value").TextContent.Should().Be("1");
        page.FindAll(".la-cards .la-card").Should().HaveCount(6);
        page.Find($"#person-{FakeAdministrationGateway.Me}").TextContent.Should().Contain("Camille Démo (vous)").And.Contain("Administrateur du cabinet");
        page.Find($"#person-{FakeAdministrationGateway.Ines}").TextContent.Should().Contain("Invitation en attente");
    }

    [Fact]
    public void Chercher_et_filtrer_reduisent_la_liste()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#kpi-pending").Click();
        page.FindAll(".la-cards .la-card").Select(c => c.QuerySelector("strong")!.TextContent).Should().Equal("Inès Bernard");

        page.Find("#filter-all").Click();
        page.Find("#people-search").Input("commerçants");
        page.FindAll(".la-cards .la-card").Select(c => c.QuerySelector("strong")!.TextContent).Should().BeEquivalentTo("Léa Martin", "Hugo Lefèvre");

        page.Find("#people-search").Input("zzz");
        page.Markup.Should().Contain("Personne pour l'instant");
    }

    [Fact]
    public void La_fiche_dit_en_clair_ce_que_la_personne_peut_faire_et_d_ou_ca_vient()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find($"#person-{FakeAdministrationGateway.Hugo}").Click();

        page.Find(".la-drawer h2").TextContent.Should().Be("Hugo Lefèvre");
        page.Find("#person-can").TextContent.Should().Be("Ce que Hugo peut faire");
        page.Find(".la-drawer .la-areas").TextContent.Should().Contain("Devis").And.Contain("Peut créer et modifier");
        page.Find(".la-row.is-inherited").TextContent.Should().Contain("Collaborateur").And.Contain("par l'équipe Pôle commerçants, sur ses dossiers");
    }

    [Fact]
    public void Donner_un_acces_se_fait_en_trois_questions_et_se_resume_en_une_phrase()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        page.Find($"#person-{FakeAdministrationGateway.Nadia}").Click();
        page.Find("#person-grant").Click();

        page.FindAll(".la-role-choice").First(c => c.TextContent.Contains("Auditeur", StringComparison.Ordinal)).QuerySelector("input")!.Change("auditeur");

        page.Find(".la-summary").TextContent.Should().Contain("Nadia sera Auditeur sur tout le cabinet, jusqu")
            .And.Contain("jusqu'au", "l'auditeur exige une date de fin : l'écran en propose une");
        page.FindAll("input[name=grant-until]")[0].HasAttribute("disabled").Should().BeTrue();

        page.Find("#grant-until-date").Change(DateTime.Today.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        page.Find("#grant-submit").Click();

        _gateway.Calls.Should().Contain($"assign {FakeAdministrationGateway.Nadia} auditeur Cabinet");
        page.Find(".la-toast").TextContent.Should().Contain("Nadia est maintenant Auditeur");
    }

    [Fact]
    public void Un_retrait_se_defait_depuis_le_message_qui_le_confirme()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        page.Find($"#person-{FakeAdministrationGateway.Nadia}").Click();

        page.Find(".la-drawer .la-row button").Click();

        page.Find(".la-toast").TextContent.Should().Contain("Rôle « Collaborateur » retiré à Nadia");
        page.FindAll(".la-drawer .la-row").Should().BeEmpty();

        page.Find(".la-toast-action").Click();

        page.WaitForAssertion(() => page.FindAll(".la-drawer .la-row").Should().ContainSingle().Which.TextContent.Should().Contain("Collaborateur"));
        _gateway.Calls.Should().Contain($"assign {FakeAdministrationGateway.Nadia} collaborateur Groupe");
    }

    [Fact]
    public void Ajouter_une_personne_enchaine_sur_son_premier_acces()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-invite").Click();
        page.Find("#person-name").Change("Zoé Petit");
        page.Find("#person-email").Change("zoe.petit@cabinet-demo.example");
        page.Find("#person-form").Submit();

        _gateway.Calls.Should().Contain("invite zoe.petit@cabinet-demo.example");
        page.Find(".la-drawer h2").TextContent.Should().Be("Donner un accès à Zoé");
        page.FindAll(".la-cards .la-card").Should().HaveCount(7);
    }

    [Theory]
    [InlineData(InvitationEmailStatusDto.Sent, "Zoé Petit est ajouté(e) et a reçu une invitation par e-mail.")]
    [InlineData(InvitationEmailStatusDto.NotConfigured, "Zoé Petit est ajouté(e). Choisissez")]
    [InlineData(InvitationEmailStatusDto.Failed, "L'e-mail d'invitation n'est pas parti pour Zoé Petit.")]
    public void Le_message_d_ajout_dit_si_l_invitation_est_partie_par_e_mail(InvitationEmailStatusDto status, string expected)
    {
        _gateway.InvitationEmail = status;
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-invite").Click();
        page.Find("#person-name").Change("Zoé Petit");
        page.Find("#person-email").Change("zoe.petit@cabinet-demo.example");
        page.Find("#person-form").Submit();

        page.WaitForAssertion(() => page.FindAll(".la-toast span").Select(t => t.TextContent).Should().Contain(t => t.StartsWith(expected, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(InvitationEmailStatusDto.Sent, "Invitation renvoyée à Inès (")]
    [InlineData(InvitationEmailStatusDto.NotConfigured, "Cette installation n'envoie pas d'e-mails : prévenez Inès vous-même.")]
    [InlineData(InvitationEmailStatusDto.Failed, "L'e-mail d'invitation n'est pas parti pour Inès.")]
    public void Une_invitation_en_attente_se_renvoie_depuis_la_fiche(InvitationEmailStatusDto status, string expected)
    {
        _gateway.InvitationEmail = status;
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        page.Find($"#person-{FakeAdministrationGateway.Ines}").Click();

        page.Find("#person-resend").Click();

        _gateway.Calls.Should().Contain($"resend {FakeAdministrationGateway.Ines}");
        page.WaitForAssertion(() => page.FindAll(".la-toast span").Select(t => t.TextContent).Should().Contain(t => t.StartsWith(expected, StringComparison.Ordinal)));
    }

    [Fact]
    public void Une_adresse_deja_prise_s_explique_sous_le_champ()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-invite").Click();
        page.Find("#person-name").Change("Léa bis");
        page.Find("#person-email").Change("lea.martin@cabinet-demo.example");
        page.Find("#person-form").Submit();

        page.Find("#person-form").TextContent.Should().Contain("Une personne du cabinet utilise déjà cette adresse.");
    }

    [Fact]
    public void L_administrateur_ne_peut_pas_se_suspendre_lui_meme()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find($"#person-{FakeAdministrationGateway.Me}").Click();

        page.Find("#person-suspend").HasAttribute("disabled").Should().BeTrue();
        page.Find(".la-drawer").TextContent.Should().Contain("C'est vous");
    }

    [Fact]
    public void Un_role_sur_mesure_se_compose_page_par_page()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        page.Find("#tab-roles").Click();
        page.Find("#role-new").Click();

        page.Find("#role-name").Input("Assistant facturation");
        page.Find("#role-quotes-work").Click();
        page.Find("#role-invoices-work").Click();
        page.Find("#role-parties-view").Click();
        page.Find("#role-editor").TextContent.Should().Contain("se consultent ensemble");
        page.Find("#role-save").Click();

        _gateway.LastRole!.Name.Should().Be("Assistant facturation");
        _gateway.LastRole.Areas.Should().BeEquivalentTo(new AreaAccess[]
        {
            new(AccessAreaDto.Quotes, AccessLevelDto.Work),
            new(AccessAreaDto.Invoices, AccessLevelDto.Work),
            new(AccessAreaDto.CreditNotes, AccessLevelDto.View),
            new(AccessAreaDto.Parties, AccessLevelDto.View),
        });
        page.Find(".la-toast").TextContent.Should().Contain("Rôle « Assistant facturation » enregistré");
    }

    [Fact]
    public void Une_equipe_se_cree_puis_recoit_membres_et_dossiers()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-new-team").Click();
        page.Find("#new-team-name").Input("Pôle artisans");
        page.Find("#new-team-submit").Click();

        page.Find(".la-drawer h2").TextContent.Should().Be("Pôle artisans");
        page.Find("#team-add-member").Change(FakeAdministrationGateway.Hugo.ToString());
        page.Find("#team-add-dossier").Change(FakeAdministrationGateway.Garage.ToString());

        page.Find("#team-members").TextContent.Should().Be("Membres (1)");
        page.Find("#team-dossiers").TextContent.Should().Be("Dossiers (1)");
        page.Find(".la-toast:last-child").TextContent.Should().Contain("Dossier Garage du Centre ajouté à l'équipe Pôle artisans");
    }

    [Fact]
    public void Le_tableau_complet_montre_chaque_role_page_par_page()
    {
        IRenderedComponent<AdminPage> page = Render<AdminPage>();
        page.Find("#tab-roles").Click();

        page.FindAll(".la-matrix thead th").Select(th => th.TextContent).Should().Contain(["Page", "Responsable", "Collaborateur", "Assistant devis"]);
        page.FindAll(".la-matrix tbody tr").Should().HaveCount(AccessAreas.All.Count);
    }

    [Fact]
    public void Sans_droit_d_administration_la_page_l_explique_simplement()
    {
        _gateway.CanAdminister = false;

        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-denied").TextContent.Should().Contain("Cet espace est réservé aux responsables");
        page.FindAll("#admin-invite").Should().BeEmpty();
    }

    [Fact]
    public void Sur_un_poste_isole_tout_est_en_lecture_avec_une_explication()
    {
        _gateway.ReadOnly = true;

        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("#admin-readonly").TextContent.Should().Contain("Sur ce poste");
        page.FindAll("#admin-invite").Should().BeEmpty();
        page.Find($"#person-{FakeAdministrationGateway.Nadia}").Click();
        page.FindAll("#person-grant").Should().BeEmpty();
    }

    [Fact]
    public void La_page_parle_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));

        IRenderedComponent<AdminPage> page = Render<AdminPage>();

        page.Find("h1").TextContent.Should().Be("Team and access");
        page.Find($"#person-{FakeAdministrationGateway.Ines}").TextContent.Should().Contain("Invitation pending");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Le_menu_ne_montre_l_entree_qu_a_qui_administre(bool canAdminister)
    {
        _gateway.CanAdminister = canAdminister;

        IRenderedComponent<NavMenu> menu = Render<NavMenu>();

        menu.FindAll("a[href='admin']").Should().HaveCount(canAdminister ? 1 : 0);
        menu.FindAll("a[href='quotes']").Should().ContainSingle("les autres entrées restent affichées : le serveur décide");
    }
}
