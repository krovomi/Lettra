using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Keyboard;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Billing;

[Collection("culture")]
public sealed class InvoiceEditorTests : BunitContext
{
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeBillingSettingsGateway _settings = new();

    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };

    public InvoiceEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IBillingSettingsGateway>(_settings);
        Services.AddSingleton<ICatalogGateway>(new FakeCatalogGateway());
        Services.AddSingleton<ICustomersGateway>(new FakeCustomersGateway());
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<InvoiceEditor> Ouvrir(Guid id) =>
        Render<InvoiceEditor>(p => p.Add(e => e.Id, id).Add(e => e.AutosaveDelay, TimeSpan.Zero));

    [Fact]
    public void Valider_puis_emettre_une_facture()
    {
        InvoiceResponse facture = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Conseil);
        IRenderedComponent<InvoiceEditor> page = Ouvrir(facture.Id);
        page.Find("h1").TextContent.Should().StartWith("Facture en brouillon");

        page.Find("#validate").Click();
        page.WaitForAssertion(() => page.Find("[role=status].lettra-success").TextContent.Should().Be("Facture validée sous le numéro FAC-2026-0042."));
        page.Find("#issue").Click();

        page.WaitForAssertion(() => page.Find(".lettra-status").TextContent.Should().Be("Émise"));
        page.Find("[role=status].lettra-success").TextContent.Should().Be("Pièce FAC-2026-0042 marquée émise.");
        page.Find("#credit-note").Should().NotBeNull("une facture émise se corrige par un avoir");
    }

    [Fact]
    public void Un_avoir_se_cree_avec_son_motif_depuis_la_facture()
    {
        InvoiceResponse facture = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042", Conseil);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"invoices/{facture.Id}?action=credit-note");
        IRenderedComponent<InvoiceEditor> page = Ouvrir(facture.Id);
        page.Find("[role=status].lettra-success").TextContent.Should().Be("Indiquez le motif puis créez l'avoir.");

        page.Find("#credit-reason").Change("Erreur de quantité");
        page.Find("#credit-note").Click();

        (Guid id, CreateCreditNoteRequest requete) = _invoices.CreditNotes.Should().ContainSingle().Subject;
        id.Should().Be(facture.Id);
        requete.Reason.Should().Be("Erreur de quantité");
        Guid avoir = _invoices.Invoices.Values.Single(i => i.Kind == InvoiceKindDto.CreditNote).Id;
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/invoices/{avoir}");
    }

    [Fact]
    public void Les_lignes_d_un_avoir_sont_en_lecture_seule_et_rappellent_la_facture()
    {
        InvoiceResponse avoir = _invoices.Add(InvoiceKindDto.CreditNote, InvoiceStatusDto.Draft, null, Conseil) with { CreditedInvoiceNumber = "FAC-2026-0042" };
        _invoices.Invoices[avoir.Id] = avoir;

        IRenderedComponent<InvoiceEditor> page = Ouvrir(avoir.Id);

        page.Find("h1").TextContent.Should().StartWith("Avoir en brouillon");
        page.Markup.Should().Contain("Avoir sur la facture FAC-2026-0042.");
        page.FindAll("input.lg-input").Should().OnlyContain(i => i.HasAttribute("readonly") || i.HasAttribute("disabled"));
        page.Find("#vat-regime").HasAttribute("disabled").Should().BeTrue();
        page.Find("#payment-terms").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Un_avoir_au_dela_du_seuil_est_approuve_par_un_manager_puis_valide()
    {
        _invoices.CreditNotesNeedApproval = true;
        InvoiceResponse avoir = _invoices.Add(InvoiceKindDto.CreditNote, InvoiceStatusDto.Draft, null, Conseil);
        IRenderedComponent<InvoiceEditor> page = Ouvrir(avoir.Id);
        page.FindAll("#approve-credit").Should().BeEmpty("rien n'indique encore qu'une approbation est exigée");

        page.Find("#validate").Click();
        page.WaitForAssertion(() => page.Find("#approve-credit"));
        page.Markup.Should().Contain("Cet avoir dépasse le seuil du cabinet : il doit être approuvé par un manager.");

        page.Find("#approve-credit").Click();
        page.WaitForAssertion(() => page.Find("[role=status].lettra-success").TextContent.Should().Be("Avoir approuvé : il peut maintenant être validé."));
        page.Find("#credit-approved").TextContent.Should().Be("Avoir approuvé par un manager le 15/10/2026.");
        page.FindAll("#approve-credit").Should().BeEmpty();

        page.Find("#validate").Click();
        page.WaitForAssertion(() => _invoices.Invoices[avoir.Id].Status.Should().Be(InvoiceStatusDto.Validated));
    }

    [Fact]
    public void L_auteur_de_l_avoir_ne_peut_pas_l_approuver()
    {
        _invoices.CreditNotesNeedApproval = true;
        _invoices.ApprovalError = ErrorCodes.SegregationOfDuties.ApproverIsCreator;
        InvoiceResponse avoir = _invoices.Add(InvoiceKindDto.CreditNote, InvoiceStatusDto.Draft, null, Conseil);
        IRenderedComponent<InvoiceEditor> page = Ouvrir(avoir.Id);
        page.Find("#validate").Click();
        page.WaitForAssertion(() => page.Find("#approve-credit"));

        page.Find("#approve-credit").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("La séparation des tâches est active : l'auteur de l'avoir ne peut pas l'approuver lui-même."));
        _invoices.Invoices[avoir.Id].CreditApprovedBy.Should().BeNull();
        page.Find("#approve-credit").Should().NotBeNull("un manager peut encore l'approuver");
    }
}

[Collection("culture")]
public sealed class BillingListTests : BunitContext
{
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();

    public BillingListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void La_page_des_avoirs_ne_liste_que_les_avoirs()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        _invoices.Add(InvoiceKindDto.CreditNote, InvoiceStatusDto.Draft, null);
        Services.GetRequiredService<NavigationManager>().NavigateTo("credit-notes");

        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();

        page.Find("h1").TextContent.Should().Be("Avoirs");
        _invoices.ListCalls.Should().ContainSingle().Which.Kind.Should().Be(InvoiceKindDto.CreditNote);
        page.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("Brouillon");
    }

    [Fact]
    public void La_recherche_filtre_les_factures()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0043");
        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();
        page.FindAll("tbody tr").Should().HaveCount(2);

        page.Find("#invoices-search").Change("0043");

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle());
        _invoices.ListCalls.Last().Search.Should().Be("0043");
    }

    [Fact]
    public void Un_devis_expire_s_affiche_comme_tel()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001");
        _quotes.Quotes[devis.Id] = devis with { IsExpired = true };

        IRenderedComponent<QuoteList> page = Render<QuoteList>();

        page.Find("tbody .lettra-status").TextContent.Should().Be("Expiré");
        page.Find("tbody a").GetAttribute("href").Should().Be($"quotes/{devis.Id}");
    }
}

[Collection("culture")]
public sealed class CompanySettingsTests : BunitContext
{
    private readonly FakeBillingSettingsGateway _settings = new();

    public CompanySettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IBillingSettingsGateway>(_settings);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    [Fact]
    public void La_fiche_existante_se_modifie_avec_sa_version()
    {
        IRenderedComponent<CompanySettings> page = Render<CompanySettings>();
        page.Find("#company-name").GetAttribute("value").Should().Be("Cabinet Martin");

        page.Find("#company-rcs").Change("RCS Lyon");
        page.Find("form").Submit();

        (SaveCompanyProfileRequest requete, long? version) = _settings.Saves.Should().ContainSingle().Subject;
        version.Should().Be(3);
        requete.Rcs.Should().Be("RCS Lyon");
        requete.Iban.Should().Be("FR7630006000011234567890189");
        page.Find("[role=status]").TextContent.Should().Be("Fiche entreprise enregistrée.");
    }

    [Fact]
    public void Sans_fiche_le_formulaire_est_vide_et_sans_erreur()
    {
        _settings.Company = null;

        IRenderedComponent<CompanySettings> page = Render<CompanySettings>();

        page.Find("#company-name").GetAttribute("value").Should().BeNullOrEmpty();
        page.FindAll("[role=alert]").Should().BeEmpty();
        page.Find("#company-country").GetAttribute("value").Should().Be("FR");
    }
}

[Collection("culture")]
public sealed class CommandPaletteTests : BunitContext
{
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeCustomersGateway _customers = new();

    public CommandPaletteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddSingleton<ICustomersGateway>(_customers);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private async Task<IRenderedComponent<CommandPalette>> Ouvrir()
    {
        IRenderedComponent<CommandPalette> palette = Render<CommandPalette>(p => p.Add(c => c.SearchDelay, TimeSpan.Zero));
        await palette.InvokeAsync(palette.Instance.OpenAsync);
        return palette;
    }

    private static IReadOnlyList<string> Options(IRenderedComponent<CommandPalette> palette) =>
        [.. palette.FindAll("li[role=option] span").Select(s => s.TextContent)];

    [Fact]
    public async Task A_l_ouverture_la_palette_propose_actions_et_ecrans()
    {
        IRenderedComponent<CommandPalette> palette = await Ouvrir();

        Options(palette).Should().StartWith(["Nouveau devis", "Nouvelle facture", "Passer en densité Confort ou Expert"]);
        Options(palette).Should().Contain(["Factures", "Avoirs", "Entreprise"]);
        palette.FindAll(".lettra-palette-group").Select(g => g.TextContent).Should().StartWith(["Actions", "Écrans"]);
    }

    [Fact]
    public async Task Taper_un_numero_propose_d_ouvrir_la_facture_ou_d_en_faire_un_avoir()
    {
        InvoiceResponse facture = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        IRenderedComponent<CommandPalette> palette = await Ouvrir();

        palette.Find("input").Input("avoir 0042");

        palette.WaitForAssertion(() => Options(palette).Should().Equal("Avoir sur FAC-2026-0042 · Dupont SARL"));
        palette.Find("input").KeyDown("Enter");
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/invoices/{facture.Id}?action=credit-note");
        palette.Instance.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Un_client_se_trouve_sans_accent_et_propose_une_nouvelle_facture()
    {
        Guid id = Guid.NewGuid();
        _customers.Customers.Add(new CustomerSummary(id, CustomerKindDto.Company, "Société Générale d'Études", null, "Paris"));
        IRenderedComponent<CommandPalette> palette = await Ouvrir();

        palette.Find("input").Input("facture societe");
        palette.WaitForAssertion(() => Options(palette).Should().Contain("Nouvelle facture pour Société Générale d'Études"));
        palette.Find("input").KeyDown("ArrowDown");
        palette.Find("input").KeyDown("ArrowUp");
        palette.Find("li[aria-selected=true] span").TextContent.Should().Be(Options(palette)[0]);
    }

    [Fact]
    public async Task Echap_ferme_la_palette()
    {
        IRenderedComponent<CommandPalette> palette = await Ouvrir();

        palette.Find("input").KeyDown("Escape");

        palette.FindAll(".lettra-palette").Should().BeEmpty();
    }
}

[Collection("culture")]
public sealed class DensityTests : BunitContext
{
    public DensityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
    }

    [Fact]
    public async Task Sans_preference_un_ecran_tactile_passe_en_Confort()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(UiModule.Path);
        module.Setup<string?>("readPreference", _ => true).SetResult(null);
        module.Setup<bool>("prefersComfort").SetResult(true);
        UiPreferences preferences = Services.GetRequiredService<UiPreferences>();

        await preferences.LoadAsync();

        preferences.Density.Should().Be(UiDensity.Comfort);
    }

    [Fact]
    public async Task La_preference_enregistree_l_emporte_et_le_basculement_la_memorise()
    {
        BunitJSModuleInterop module = JSInterop.SetupModule(UiModule.Path);
        module.Setup<string?>("readPreference", _ => true).SetResult("Expert");
        module.Setup<bool>("prefersComfort").SetResult(true);
        module.SetupVoid("writePreference", _ => true).SetVoidResult();
        UiPreferences preferences = Services.GetRequiredService<UiPreferences>();
        int changements = 0;
        preferences.Changed += (_, _) => changements++;

        await preferences.LoadAsync();
        preferences.Density.Should().Be(UiDensity.Expert);
        await preferences.ToggleDensityAsync();

        preferences.Density.Should().Be(UiDensity.Comfort);
        changements.Should().Be(1);
        module.VerifyInvoke("writePreference").Arguments.Should().Equal("lettra.density", "Comfort");
    }

    [Fact]
    public async Task Sans_JavaScript_la_densite_reste_Expert()
    {
        UiPreferences preferences = Services.GetRequiredService<UiPreferences>();

        await preferences.LoadAsync();

        preferences.Density.Should().Be(UiDensity.Expert);
    }
}
