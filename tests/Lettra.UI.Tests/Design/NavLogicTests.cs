using Bunit;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Onboarding;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Design;

/// <summary>Logique du menu et de l'aide, sans rendu : recherche, réglage retenu, clés par utilisateur.</summary>
public sealed class NavLogicTests : BunitContext
{
    private static readonly NavItem Factures = new("invoices", "Factures", "receipt", "Ventes");
    private static readonly NavItem Recurrentes = new("recurring-invoices", "Factures récurrentes", "repeat", "Ventes");
    private static readonly NavItem Declarations = new("tax", "Déclarations", "calendar-clock", "Compta");

    private static string? MotsCles(NavItem item) => item == Declarations ? "tva impôts facture" : null;

    [Fact]
    public void La_recherche_du_menu_met_les_libelles_devant_les_mots_cles()
    {
        NavSearch.Filter([Declarations, Factures, Recurrentes], "facture", i => i.LabelKey, MotsCles)
            .Should().Equal(Factures, Recurrentes, Declarations);
        NavSearch.Filter([Declarations, Factures], "TVA", i => i.LabelKey, MotsCles).Should().Equal(Declarations);
        NavSearch.Filter([Declarations, Factures], "declar", i => i.LabelKey, MotsCles).Should().Equal(Declarations);
        NavSearch.Filter([Declarations, Factures], "  ", i => i.LabelKey, MotsCles).Should().Equal(Declarations, Factures);
        NavSearch.Score("Factures", null, "zzz").Should().Be(0);
    }

    [Theory]
    [InlineData("invoices", "invoices", true)]
    [InlineData("invoices", "invoices/42?x=1", true)]
    [InlineData("invoices", "invoices-archive", false)]
    [InlineData("", "", true)]
    [InlineData("", "quotes", false)]
    public void Une_entree_reconnait_l_ecran_affiche(string href, string chemin, bool attendu) =>
        new NavItem(href, "X", "x", null).IsCurrent(chemin).Should().Be(attendu);

    [Fact]
    public void Une_entree_a_correspondance_exacte_ne_couvre_pas_ses_sous_ecrans() =>
        new NavItem("settings/company", "X", "x", null, Match: NavLinkMatch.All).IsCurrent("settings/company/logo").Should().BeFalse();

    [Fact]
    public void La_cle_d_un_utilisateur_ne_montre_pas_son_nom()
    {
        UserStorageKey.Hash(null).Should().Be("811c9dc5");
        UserStorageKey.Hash("Local|Claire Martin").Should().HaveLength(8).And.NotBe(UserStorageKey.Hash("Local|Paul Durand"));
        UserStorageKey.UserOf(new SessionResponse(true, SessionModeDto.Oidc, "Claire", true)).Should().Be("Oidc|Claire");
        UserStorageKey.UserOf(new SessionResponse(false, SessionModeDto.Oidc, null, true)).Should().BeNull();
        GuidedTours.StorageKey("Local|Claire Martin").Should().Be("lettra.tours." + UserStorageKey.Hash("Local|Claire Martin"));
    }

    [Fact]
    public void Les_mots_utiles_d_une_question_perdent_les_petits_mots_et_leur_fin()
    {
        HelpCatalog.Words("Comment je peux annuler ma facture ?").Should().Equal("annul", "factu");
        HelpCatalog.Words("TVA").Should().Equal("tva");
        HelpCatalog.Words("  ").Should().BeEmpty();
        HelpCatalog.Search("  ", k => k).Should().HaveCount(HelpCatalog.All.Count);
        HelpCatalog.Search("comment je", k => k).Should().BeEmpty("une question faite de petits mots ne trouve rien");
    }

    [Fact]
    public void Chaque_mot_exige_tous_les_mots()
    {
        static string Texte(string cle) => cle == "Help.Faq.fix-invoice.Question" ? "Corriger une facture" : string.Empty;

        HelpCatalog.Search("corriger facture", Texte, everyWord: true).Select(e => e.Id).Should().Equal("fix-invoice");
        HelpCatalog.Search("corriger 0042", Texte, everyWord: true).Should().BeEmpty();
        HelpCatalog.Search("corriger 0042", Texte).Select(e => e.Id).Should().Equal("fix-invoice");
    }

    [Fact]
    public void Les_questions_d_un_ecran_suivent_ses_routes()
    {
        HelpCatalog.ForPath("tax/vat").Select(e => e.Id).Should().Equal("vat", "fec");
        HelpCatalog.ForPath("ecran-inconnu").Should().BeEmpty();
        HelpCatalog.Find("assistant")!.AppliesTo("").Should().BeFalse();
    }

    [Fact]
    public async Task Le_reglage_du_menu_sans_session_vaut_pour_l_appareil()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        BunitJSModuleInterop module = JSInterop.SetupModule(UiModule.Path);
        module.Setup<string?>("readPreference", "lettra.nav.favorites.811c9dc5").SetResult("catalog,inconnu,catalog");
        Services.AddLettraUi();
        NavMenuState menu = Services.GetRequiredService<NavMenuState>();
        int changements = 0;
        menu.Changed += (_, _) => changements++;

        await menu.LoadAsync();
        await menu.LoadAsync();

        menu.Favorites.Should().Equal("catalog");
        changements.Should().Be(1, "une seconde lecture pour le même utilisateur ne relit rien");
        await menu.ToggleSectionAsync("B");
        await menu.ToggleSectionAsync("A");
        menu.IsCollapsed("B").Should().BeTrue();
        await menu.ToggleSectionAsync("A");
        menu.IsCollapsed("A").Should().BeFalse();
        module.Invocations.Last(i => i.Identifier == "writePreference").Arguments.Should().Equal("lettra.nav.sections.811c9dc5", "B");
        await menu.ToggleFavoriteAsync(new NavItem("bank", "Banque", "landmark", null));
        menu.FavoriteItems(NavItems.All).Select(i => i.Href).Should().Equal("catalog", "bank");
    }

    [Fact]
    public async Task Un_autre_utilisateur_retrouve_son_propre_menu()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        BunitJSModuleInterop module = JSInterop.SetupModule(UiModule.Path);
        module.Setup<string?>("readPreference", "lettra.nav.favorites." + UserStorageKey.Hash("Local|Claire Martin")).SetResult("customers");
        module.Setup<string?>("readPreference", "lettra.nav.favorites." + UserStorageKey.Hash("Local|Paul Durand")).SetResult("bank");
        FakeSessionGateway sessions = new(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true));
        Services.AddSingleton<ISessionGateway>(sessions);
        Services.AddLettraUi();
        NavMenuState menu = Services.GetRequiredService<NavMenuState>();
        SessionState session = Services.GetRequiredService<SessionState>();

        await session.RefreshAsync();
        await menu.LoadAsync();
        menu.Favorites.Should().Equal("customers");

        await sessions.CreateLocalAccountAsync(new CreateLocalAccountRequest { DisplayName = "Paul Durand", Passphrase = "phrase-secrete-longue" }, CancellationToken.None);
        await session.RefreshAsync();

        menu.Favorites.Should().Equal("bank");
    }
}
