using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Billing;

internal sealed class FakeFileDownloader : IFileDownloader
{
    public List<(string FileName, string MimeType, byte[] Content)> Files { get; } = [];

    public Task DownloadAsync(string fileName, string mimeType, byte[] content, CancellationToken cancellationToken)
    {
        Files.Add((fileName, mimeType, content));
        return Task.CompletedTask;
    }
}

[Collection("culture")]
public sealed class QuoteEditorTests : BunitContext
{
    private const int Reference = 0, Designation = 1, Quantite = 2, Prix = 4;

    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeBillingSettingsGateway _settings = new();
    private readonly FakeCatalogGateway _catalog = new();
    private readonly FakeCustomersGateway _customers = new();
    private readonly FakePriceListsGateway _prices = new();
    private readonly FakeFileDownloader _files = new();

    private static readonly DocumentLineRequest Conseil = new() { Designation = "Conseil", UnitCode = "HUR", Quantity = 2m, UnitPriceExclTax = 100m };
    private static readonly DocumentLineRequest Livre = new() { Designation = "Ouvrage", UnitCode = "C62", Quantity = 1m, UnitPriceExclTax = 30m, VatRateCode = "FR-5.5" };

    public QuoteEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<IBillingSettingsGateway>(_settings);
        Services.AddSingleton<ICatalogGateway>(_catalog);
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<IPriceListsGateway>(_prices);
        Services.AddSingleton<IFileDownloader>(_files);
        _customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private static string Fr(decimal value) => GridNumber.FormatAmount(value, CultureInfo.GetCultureInfo("fr-FR"));

    private IRenderedComponent<QuoteEditor> Ouvrir(Guid? id = null) =>
        Render<QuoteEditor>(p => p.Add(e => e.Id, id).Add(e => e.AutosaveDelay, TimeSpan.Zero));

    private static Task<CellCommitResult> Saisir(IRenderedComponent<QuoteEditor> page, int row, int col, string text)
    {
        IRenderedComponent<DataEntryGrid<DocumentLineRow>> grille = page.FindComponent<DataEntryGrid<DocumentLineRow>>();
        return page.InvokeAsync(() => grille.Instance.OnCellCommitted(row, col, text));
    }

    private static string Texte(IRenderedComponent<QuoteEditor> page, string selector) => page.Find(selector).TextContent.Trim();

    [Fact]
    public void Le_pdf_est_propose_dans_la_langue_du_client()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        _customers.Languages[BillingFixtures.DupontId] = "en-US";

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.Find("#pdf-language").GetAttribute("value").Should().Be("en-US"));
    }

    [Fact]
    public void Les_champs_de_l_en_tete_et_les_notes_sont_des_champs_empiles_et_les_colonnes_des_lignes_sont_souples()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        foreach (string champ in new[] { "#valid-until", "#vat-regime", "#subject", "#notes" })
        {
            // Un champ assisté (objet, notes) est enveloppé (suggestions, micro) : le champ empilé est le parent de l'enveloppe.
            AngleSharp.Dom.IElement element = page.Find(champ);
            AngleSharp.Dom.IElement parent = (element.Closest(".lt-assist") ?? element).ParentElement!;
            parent.ClassList.Should().Contain("lettra-field", $"{champ} a son libellé au-dessus, sur toute la largeur de sa colonne");
        }

        page.Find("label[for=notes]").ParentElement!.ClassList.Should().Contain("lettra-field");
        // Chaque colonne, sauf « Remise % » déjà à sa largeur minimale, a une largeur minimale et maximale : la grille
        // tient dès 720 px, sans défilement horizontal.
        Regex.Matches(page.Find(".lg-grid").GetAttribute("style")!, @"minmax\(").Should().HaveCount(7);
    }

    [Fact]
    public void Les_blocs_se_replient_et_resument_leur_contenu_et_les_notes_vides_partent_repliees()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil, Livre);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(2));
        page.Find("#quote-main-toggle").GetAttribute("aria-expanded").Should().Be("true");
        page.Find("#quote-notes-toggle").GetAttribute("aria-expanded").Should().Be("false", "sans note, le bloc ne prend qu'une ligne");
        page.Find("#quote-notes .lt-collapsible-summary").TextContent.Should().Be("Aucune note");

        page.Find("#quote-lines-toggle").Click();
        page.Find("#quote-main-toggle").Click();

        page.Find("#quote-lines .lt-collapsible-summary").TextContent.Should().Be($"2 ligne(s) · {Fr(230m)} HT");
        page.Find("#quote-main .lt-collapsible-summary").TextContent.Should().StartWith("Dupont SARL · ").And.EndWith("TVA française");
        page.FindAll(".lg-row[data-r]").Should().HaveCount(2, "replié, la grille garde ses lignes");
    }

    [Fact]
    public void Un_brouillon_s_ouvre_avec_ses_lignes_ses_totaux_et_sa_checklist()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil, Livre);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        Texte(page, "h1").Should().StartWith("Devis en brouillon");
        page.FindAll(".lg-row[data-r]").Should().HaveCount(2);
        Texte(page, "#total-excl-tax").Should().Be(Fr(230m));
        Texte(page, "#total-vat").Should().Be(Fr(41.65m));
        Texte(page, "#total-incl-tax").Should().Be(Fr(271.65m));
        page.FindAll(".lettra-vat tbody tr").Select(r => r.Children[0].TextContent).Should().Equal("20 %", "5,5 %");
        Texte(page, ".lettra-checklist-summary").Should().Be("Prête à valider : toutes les mentions obligatoires sont là.");
        page.Find("#validate").HasAttribute("disabled").Should().BeFalse();
        _quotes.Checks.Should().Equal(devis.Id);
    }

    [Fact]
    public async Task Modifier_une_quantite_recalcule_les_totaux_et_enregistre_le_brouillon_avec_sa_version()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        (await Saisir(page, 0, Quantite, "3,5")).Ok.Should().BeTrue();

        page.WaitForAssertion(() => Texte(page, "#total-incl-tax").Should().Be(Fr(420m)));
        (Guid id, SaveQuoteRequest requete, long? version) = _quotes.Updates.Should().ContainSingle().Subject;
        id.Should().Be(devis.Id);
        version.Should().Be(1);
        requete.Lines.Should().ContainSingle().Which.Quantity.Should().Be(3.5m);
        _quotes.Checks.Should().HaveCount(2, "la checklist est recalculée après chaque enregistrement");
        this.ToastText().Should().Be("Modifications enregistrées.", "chaque modification enregistrée passe par le toaster");
    }

    [Fact]
    public void Supprimer_un_brouillon_l_annonce_dans_le_toaster_et_revient_a_la_liste()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.Find("button.lettra-link.is-danger").Click());

        page.WaitForAssertion(() => this.ToastText().Should().Be("Brouillon supprimé."));
        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/quotes");
    }

    [Fact]
    public void La_corbeille_d_une_ligne_la_retire_au_premier_clic_et_enregistre_le_brouillon()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil, Livre);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);
        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(2));

        page.Find(".lg-row[data-r='0'] .lt-row-delete").GetAttribute("aria-label").Should().Be("Supprimer la ligne 1");
        page.Find(".lg-row[data-r='0'] .lt-row-delete").Click();

        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().ContainSingle());
        Texte(page, "#total-excl-tax").Should().Be(Fr(30m));
        page.WaitForAssertion(() => _quotes.Updates.Last().Request.Lines.Should().ContainSingle().Which.Designation.Should().Be("Ouvrage"));
    }

    [Fact]
    public void Un_devis_valide_n_a_pas_de_corbeille_sur_ses_lignes()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001", Conseil);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(1));
        page.FindAll(".lt-row-delete").Should().BeEmpty();
    }

    [Fact]
    public void Une_checklist_bloquante_empeche_la_validation_et_liste_les_constats()
    {
        _quotes.Report = BillingFixtures.Blocking;
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        Texte(page, ".lettra-checklist-summary").Should().Be("2 point(s) bloquent la validation.");
        page.Find("#validate").HasAttribute("disabled").Should().BeTrue();
        page.FindAll(".lettra-finding.is-error").Should().HaveCount(2);
        page.FindAll(".lettra-finding.is-error")[1].TextContent.Should().Contain("ligne 2");
        page.Find(".lettra-finding.is-ok button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Un_constat_sur_l_emetteur_renvoie_vers_la_fiche_entreprise()
    {
        _quotes.Report = new ComplianceReportResponse(true, [new ComplianceFindingResponse(ErrorCodes.Mention.SellerSiren, ComplianceSeverityDto.Error, null)]);
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.Find(".lettra-finding button").Click();

        Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/settings/company");
    }

    [Fact]
    public void Valider_numerote_le_devis_et_le_fige()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.Find("#validate").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Devis validé sous le numéro DEV-2026-0007."));
        Texte(page, "h1").Should().StartWith("Devis DEV-2026-0007");
        Texte(page, ".lettra-status").Should().Be("Validé");
        page.FindAll("#validate").Should().BeEmpty();
        page.Find("#accept").Should().NotBeNull();
        page.Find("#customer").HasAttribute("disabled").Should().BeTrue();
        _quotes.Validations.Should().Equal(devis.Id);
    }

    [Fact]
    public async Task Une_modification_concurrente_affiche_le_bandeau_et_suspend_l_enregistrement()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);
        _quotes.NextVersionMismatch = true;

        await Saisir(page, 0, Quantite, "4");
        page.WaitForAssertion(() => Texte(page, ".lettra-banner").Should().StartWith("Quelqu'un a modifié cette pièce entre-temps."));
        await Saisir(page, 0, Quantite, "5");

        _quotes.Updates.Should().ContainSingle("plus rien n'est envoyé tant que le devis n'est pas rechargé");
        page.Find("#validate").HasAttribute("disabled").Should().BeTrue();

        page.Find(".lettra-banner button").Click();
        page.WaitForAssertion(() => page.FindAll(".lettra-banner").Should().BeEmpty());
    }

    [Fact]
    public async Task Un_nouveau_devis_nait_avec_son_client_quand_une_ligne_a_sa_designation()
    {
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"quotes/new?customer={BillingFixtures.DupontId}");
        IRenderedComponent<QuoteEditor> page = Ouvrir();
        page.WaitForAssertion(() => Texte(page, "#customer").Should().Be("Dupont SARL"));
        Texte(page, "h1").Should().StartWith("Nouveau devis");
        IRenderedComponent<DataEntryGrid<DocumentLineRow>> grille = page.FindComponent<DataEntryGrid<DocumentLineRow>>();

        await page.InvokeAsync(() => grille.Instance.OnCommand("Insert", 0, Reference));
        await Saisir(page, 0, Prix, "1 200");
        page.WaitForAssertion(() => Texte(page, "#total-incl-tax").Should().Be(Fr(1440m)));
        _quotes.Creations.Should().BeEmpty("une ligne sans désignation ne part pas au serveur");
        await Saisir(page, 0, Designation, "Tenue de comptabilité");

        SaveQuoteRequest creation = _quotes.Creations.Should().ContainSingle().Subject;
        creation.CustomerId.Should().Be(BillingFixtures.DupontId);
        creation.Lines.Should().ContainSingle().Which.Should().Match<DocumentLineRequest>(l => l.Designation == "Tenue de comptabilité" && l.UnitPriceExclTax == 1200m);
        navigation.Uri.Should().EndWith($"/quotes/{_quotes.Quotes.Keys.Single()}");
    }

    [Fact]
    public void Le_choix_du_client_liste_tous_les_clients_du_dossier_meme_au_dela_d_une_page()
    {
        // Régression : la liste demandait 500 clients d'un coup, l'API (200 au plus) refusait et la liste restait vide.
        for (int i = 1; i <= CustomerListing.MaxPageSize + 50; i++)
        {
            _customers.Customers.Add(new CustomerSummary(Guid.NewGuid(), CustomerKindDto.Company, $"Client {i:000}", null, "Nantes"));
        }

        _customers.Customers.Add(new CustomerSummary(Guid.NewGuid(), CustomerKindDto.Individual, "Zoé Martin", null, "Brest"));
        IRenderedComponent<QuoteEditor> page = Ouvrir();

        page.Find("#customer").Click();

        page.WaitForAssertion(() => Texte(page, ".lg-hint").Should().Contain($"{CustomerListing.MaxPageSize + 52} résultat(s)"));
        page.Find("[role=dialog] input").Input("zoé");
        page.Find("[role=dialog] input").KeyDown("Enter");
        page.WaitForAssertion(() => Texte(page, "#customer").Should().Be("Zoé Martin"));
    }

    [Fact]
    public void Enregistrer_sans_client_dit_quoi_faire_au_lieu_de_rester_inactif()
    {
        IRenderedComponent<QuoteEditor> page = Ouvrir();

        page.Find("#save").HasAttribute("disabled").Should().BeFalse();
        page.Find("#save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Choisissez un client."));
        _quotes.Creations.Should().BeEmpty();
    }

    [Fact]
    public async Task Enregistrer_sans_ligne_complete_le_dit()
    {
        NavigationManager navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"quotes/new?customer={BillingFixtures.DupontId}");
        IRenderedComponent<QuoteEditor> page = Ouvrir();
        page.WaitForAssertion(() => Texte(page, "#customer").Should().Be("Dupont SARL"));

        await page.InvokeAsync(() => page.Find("#save").Click());

        page.WaitForAssertion(() => this.ToastText().Should().Be("Ajoutez au moins une ligne."));
        _quotes.Creations.Should().BeEmpty();
    }

    [Fact]
    public async Task Choisir_un_article_applique_le_tarif_du_client()
    {
        Contracts.Catalog.CatalogItemResponse article = _catalog.Add("TENUE", "Tenue mensuelle", 400m);
        _prices.Prices[article.Id] = new Contracts.Pricing.ResolvedPrice(article.Id, 350m, 5m, "FR-20", Guid.NewGuid());
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        await Saisir(page, 0, Reference, "tenue");

        page.WaitForAssertion(() => _quotes.Quotes[devis.Id].Lines.Single().UnitPriceExclTax.Should().Be(350m));
        DocumentLineResponse ligne = _quotes.Quotes[devis.Id].Lines.Single();
        ligne.Designation.Should().Be("Tenue mensuelle");
        ligne.DiscountPercent.Should().Be(5m);
        ligne.CatalogItemId.Should().Be(article.Id);
    }

    [Fact]
    public void Le_PDF_se_telecharge_dans_la_langue_choisie()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0007", Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.Find("#pdf-language").Change("en-US");
        page.Find("#pdf").Click();

        (string nom, string type, byte[] contenu) = _files.Files.Should().ContainSingle().Subject;
        nom.Should().Be("DEV-2026-0007.pdf");
        type.Should().Be("application/pdf");
        Encoding.ASCII.GetString(contenu, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Un_devis_accepte_propose_la_facture_finale_et_l_acompte()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Accepted, "DEV-2026-0007", Conseil);
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.Find("#down-payment").Change("40");
        page.Find("#create-down-payment").Click();

        _quotes.LastDownPayment!.Percent.Should().Be(40m);
        Services.GetRequiredService<NavigationManager>().Uri.Should().Contain("/invoices/");
    }

    [Fact]
    public void L_ecran_est_traduit_en_anglais()
    {
        UseCulture("en-US");
        _quotes.Report = BillingFixtures.Blocking;
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Conseil);

        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        Texte(page, "h1").Should().StartWith("Draft quote");
        page.FindAll(".lg-head").Select(h => h.TextContent)
            .Should().Equal("Ref.", "Description", "Qty", "Unit", "Unit price", "Discount %", "VAT", "Net total", "Actions");
        Texte(page, ".lettra-checklist-summary").Should().Be("2 item(s) block validation.");
        page.Find(".lg-row[data-r='0'] input.lg-input[data-c='3']").GetAttribute("value").Should().Be("Hour");
    }
}
