using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Parties;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

[Collection("culture")]
public sealed class SuppliersPageTests : BunitContext
{
    private readonly FakeSuppliersGateway _gateway = new();
    private readonly FakeDirectoryGateway _directory = new();

    public SuppliersPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ExplorerSettings.SearchDelay = TimeSpan.FromMilliseconds(1);
        Services.AddLettraUi();
        Services.AddSingleton<ISuppliersGateway>(_gateway);
        Services.AddSingleton<IDirectoryGateway>(_directory);
        UseCulture("fr-FR");
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private IRenderedComponent<SuppliersPage> Open(string route = "suppliers/new")
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<SuppliersPage> page = Render<SuppliersPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static void Fill(IRenderedComponent<SuppliersPage> page, string id, string value) => page.Find($"#{id}").Change(value);

    private static string? Value(IRenderedComponent<SuppliersPage> page, string id) => page.Find($"#{id}").GetAttribute("value");

    private static void Search(IRenderedComponent<SuppliersPage> page, string query)
    {
        page.Find("#supplier-lookup-query").Input(query);
        page.Find("button.lettra-lookup-search").Click();
    }

    [Fact]
    public void L_ecran_est_en_francais_et_traduit_en_anglais()
    {
        IRenderedComponent<SuppliersPage> page = Open("suppliers");
        page.Find("h1").TextContent.Should().Be("Fournisseurs");
        page.Markup.Should().Contain("Aucun fournisseur pour l'instant.");
        page.FindAll("form").Should().BeEmpty();

        page = Open();
        page.Find("#explorer-save").TextContent.Should().Contain("Créer");
        page.Find("button.lettra-lookup-search").TextContent.Should().Be("Rechercher dans l'annuaire");

        UseCulture("en-US");
        page = Open();
        page.Find("#explorer-save").TextContent.Should().Contain("Create");
        page.Find("button.lettra-vies-check").TextContent.Should().Be("Check (VIES)");
    }

    [Fact]
    public void Creer_un_fournisseur_l_ajoute_a_la_liste()
    {
        IRenderedComponent<SuppliersPage> page = Open();

        Fill(page, "name", "Papeterie Durand");
        Fill(page, "siren", "732829320");
        Fill(page, "iban", "FR7630006000011234567890189");
        Fill(page, "line1", "3 rue des Lilas");
        Fill(page, "zip", "69001");
        Fill(page, "city", "Lyon");
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Fournisseur « Papeterie Durand » créé."));
        _gateway.LastRequest!.Iban.Should().Be("FR7630006000011234567890189");
        Guid id = _gateway.Suppliers.Single().Id;
        Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"suppliers/{id}");
        page.Find("#suppliers-list").TextContent.Should().Contain("Papeterie Durand").And.Contain("Lyon");
        page.Find("#supplier-iban").TextContent.Should().Contain("FR76 3000 6000 0112 3456 7890 189");
    }

    [Fact]
    public void La_validation_et_les_erreurs_du_serveur_sont_traduites()
    {
        _gateway.NextErrors = [new ErrorDetail(ErrorCodes.SharedKernel.IbanInvalid, "Iban")];
        IRenderedComponent<SuppliersPage> page = Open();

        page.Find("form").Submit();
        page.Markup.Should().Contain("Le nom du fournisseur est obligatoire.");

        Fill(page, "name", "Acme");
        Fill(page, "iban", "FR00");
        Fill(page, "line1", "1 rue");
        Fill(page, "zip", "75002");
        Fill(page, "city", "Paris");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("#iban ~ .validation-message").TextContent
            .Should().Be("Cet IBAN n'est pas valide (vérifiez le pays, la longueur et la clé)."));
    }

    [Fact]
    public void Choisir_une_entreprise_de_l_annuaire_remplit_le_formulaire()
    {
        _directory.Companies.Add(FakeDirectoryGateway.Ancienne);
        _directory.Companies.Add(FakeDirectoryGateway.Danone);
        IRenderedComponent<SuppliersPage> page = Open();

        Search(page, "  sarl  ");

        _directory.Queries.Should().Equal("sarl");
        page.FindAll("[role=option]").Should().HaveCount(2);
        page.FindAll("[role=option]")[0].TextContent.Should().Contain("ANCIENNE SARL").And.Contain("SIREN 732829320").And.Contain("cessée");
        page.FindAll("[role=option]")[1].TextContent.Should().NotContain("cessée");

        page.Find("#supplier-lookup-query").KeyDown("ArrowDown");
        page.Find("#supplier-lookup-query").KeyDown("Enter");

        Value(page, "name").Should().Be("DANONE");
        Value(page, "siren").Should().Be("552032534");
        Value(page, "siret").Should().Be("55203253400646");
        Value(page, "vat").Should().Be("FR27552032534");
        Value(page, "line1").Should().Be("17 Boulevard Haussmann");
        Value(page, "zip").Should().Be("75009");
        Value(page, "city").Should().Be("Paris");
        Value(page, "country").Should().Be("FR");
        page.FindAll("[role=option]").Should().BeEmpty();
    }

    [Fact]
    public void Un_clic_sur_un_resultat_le_choisit()
    {
        _directory.Companies.Add(FakeDirectoryGateway.Danone);
        IRenderedComponent<SuppliersPage> page = Open();

        Search(page, "danone");
        page.Find("[role=option]").Click();

        Value(page, "name").Should().Be("DANONE");
    }

    [Fact]
    public void Une_recherche_trop_courte_n_interroge_pas_l_annuaire()
    {
        IRenderedComponent<SuppliersPage> page = Open();

        Search(page, "ab");

        _directory.Queries.Should().BeEmpty();
        page.Find(".lettra-lookup-error").TextContent.Should().Be("Saisissez au moins 3 caractères pour rechercher.");
    }

    [Fact]
    public void Annuaire_indisponible_message_traduit_et_saisie_manuelle_possible()
    {
        _directory.SearchError = ErrorCodes.Directory.Unavailable;
        IRenderedComponent<SuppliersPage> page = Open();

        Search(page, "danone");

        page.Find(".lettra-lookup-error").TextContent
            .Should().Be("L'annuaire est indisponible pour le moment. Vous pouvez saisir les informations à la main.");
        Fill(page, "name", "Saisie manuelle");
        Fill(page, "line1", "1 rue");
        Fill(page, "zip", "75002");
        Fill(page, "city", "Paris");
        page.Find("form").Submit();
        page.WaitForAssertion(() => page.Find("#suppliers-list").TextContent.Should().Contain("Saisie manuelle"));
    }

    [Fact]
    public void Serveur_injoignable_message_traduit_en_anglais()
    {
        UseCulture("en-US");
        _directory.SearchError = ErrorCodes.Api.Unreachable;
        IRenderedComponent<SuppliersPage> page = Open();

        Search(page, "danone");

        page.Find(".lettra-lookup-error").TextContent.Should().Be("The server cannot be reached. Check the connection.");
    }

    [Fact]
    public void La_verification_VIES_affiche_le_resultat_et_le_nom()
    {
        _directory.VatResponse = new VatNumberCheckResponse("FR27552032534", true, "DANONE", "17 BD HAUSSMANN", DateTimeOffset.UnixEpoch);
        IRenderedComponent<SuppliersPage> page = Open();
        page.Find("button.lettra-vies-check").HasAttribute("disabled").Should().BeTrue();

        Fill(page, "vat", "FR27552032534");
        page.Find("button.lettra-vies-check").Click();

        page.Find(".lettra-vies-result").TextContent.Should().Contain("Numéro valide").And.Contain("DANONE");
    }

    [Fact]
    public void La_verification_VIES_signale_un_numero_invalide_ou_une_erreur_traduite()
    {
        _directory.VatResponse = new VatNumberCheckResponse("FR00000000000", false, null, null, DateTimeOffset.UnixEpoch);
        IRenderedComponent<SuppliersPage> page = Open();
        Fill(page, "vat", "FR00000000000");
        page.Find("button.lettra-vies-check").Click();
        page.Find(".lettra-vies-result").TextContent.Trim().Should().Be("Numéro inconnu de VIES ou inactif");

        _directory.VatError = ErrorCodes.Directory.VatCountryNotSupported;
        Fill(page, "vat", "US123");
        page.FindAll(".lettra-vies-result").Should().BeEmpty();
        page.Find("button.lettra-vies-check").Click();
        page.Find(".lettra-vies-result").TextContent
            .Should().Be("La vérification VIES ne couvre que les numéros de TVA des pays de l'Union européenne.");
    }

    [Fact]
    public void La_fiche_fournisseur_se_consulte_puis_se_modifie_sans_changer_d_ecran()
    {
        Contracts.Suppliers.SupplierResponse supplier = _gateway.Add("Papeterie Durand", "Lyon", "FR7630006000011234567890189");
        IRenderedComponent<SuppliersPage> page = Open($"suppliers/{supplier.Id}");

        page.Find("#supplier-sheet-title").TextContent.Should().Be("Papeterie Durand");
        page.Find("#supplier-details").TextContent.Should().Contain("contact@example.fr").And.Contain("3 rue des Lilas");
        page.Find("#supplier-bank .lt-collapsible-toggle").Click();
        page.Find("#supplier-bank-body").HasAttribute("hidden").Should().BeTrue("le bloc bancaire est replié");
        page.Find("#supplier-bank .lt-collapsible-toggle").Click();

        Navigation.NavigateTo($"suppliers/{supplier.Id}/edit");
        page.WaitForAssertion(() => page.Find("#iban").GetAttribute("value").Should().Be("FR7630006000011234567890189"));
        Fill(page, "email", "compta@durand.fr");
        page.Find("form").Submit();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Fournisseur « Papeterie Durand » enregistré."));
        _gateway.Updates.Should().ContainSingle().Which.ExpectedVersion.Should().Be(1);
        page.Find("#supplier-details").TextContent.Should().Contain("compta@durand.fr");
    }

    [Fact]
    public void Echap_en_saisie_modifiee_demande_avant_d_abandonner()
    {
        Contracts.Suppliers.SupplierResponse supplier = _gateway.Add("Papeterie Durand", "Lyon");
        IRenderedComponent<SuppliersPage> page = Open($"suppliers/{supplier.Id}/edit");

        Fill(page, "city", "Vienne");
        page.Find("#explorer-cancel").Click();

        page.Find("#explorer-discard-yes").Click();
        page.WaitForAssertion(() => Navigation.ToBaseRelativePath(Navigation.Uri).Should().Be($"suppliers/{supplier.Id}"));
        _gateway.Records[supplier.Id].City.Should().Be("Lyon");
    }
}
