using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Typing;

/// <summary>Aide à la saisie dans les devis et factures (vague 5, lane F) : saisie rapide, F4 sur la désignation, habitudes du client.</summary>
[Collection("culture")]
public sealed class EditorAssistTests : BunitContext
{
    private const int Reference = 0, Designation = 1, Quantite = 2, Unite = 3, Prix = 4;

    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeCatalogGateway _catalog = new();
    private readonly CatalogItemResponse _tenue;

    public EditorAssistTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IBillingSettingsGateway>(new FakeBillingSettingsGateway());
        Services.AddSingleton<ICatalogGateway>(_catalog);
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(BillingFixtures.DupontId, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        Services.AddSingleton<ICustomersGateway>(customers);
        Services.AddSingleton<IFileDownloader>(new FakeFileDownloader());
        _tenue = _catalog.Add("TENUE", "Tenue comptable mensuelle", 250m);
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static DocumentLineRequest Ligne(string designation, Guid? article = null) =>
        new() { CatalogItemId = article, Reference = article is null ? null : "TENUE", Designation = designation, UnitCode = "C62", Quantity = 1m, UnitPriceExclTax = 100m };

    private IRenderedComponent<QuoteEditor> Ouvrir(Guid id) => Render<QuoteEditor>(p => p.Add(e => e.Id, id).Add(e => e.AutosaveDelay, TimeSpan.Zero));

    private static IRenderedComponent<DataEntryGrid<DocumentLineRow>> Grille(IRenderedComponent<QuoteEditor> page) =>
        page.FindComponent<DataEntryGrid<DocumentLineRow>>();

    [Fact]
    public async Task La_quantite_en_heures_ou_en_jours_change_l_unite_et_le_prix_accepte_un_produit()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Ligne("Conseil"));
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);
        var grille = Grille(page);

        (await page.InvokeAsync(() => grille.Instance.OnCellCommitted(0, Quantite, "1h30"))).Text.Should().Be("1,5");
        (await page.InvokeAsync(() => grille.Instance.OnCellCommitted(0, Prix, "2x45"))).Text.Should().Be("90,00");

        page.WaitForAssertion(() => page.Find($"input.lg-input[data-r='0'][data-c='{Unite}']").GetAttribute("value").Should().Be("Heure"));
        page.Find("#total-excl-tax").TextContent.Trim().Should().Be(GridNumber.FormatAmount(135m, CultureInfo.CurrentCulture));
        _quotes.Updates.Last().Request.Lines.Single().Should().Match<DocumentLineRequest>(l => l.Quantity == 1.5m && l.UnitCode == "HUR" && l.UnitPriceExclTax == 90m);

        (await page.InvokeAsync(() => grille.Instance.OnCellCommitted(0, Quantite, "2 j"))).Ok.Should().BeTrue();
        _quotes.Updates.Last().Request.Lines.Single().UnitCode.Should().Be("DAY");
    }

    [Fact]
    public void L_apercu_de_la_saisie_rapide_ne_parle_que_des_formes_rapides()
    {
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Ligne("Conseil"));
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);
        var lignes = page.FindComponent<Lettra.UI.Shared.Components.Billing.DocumentLinesGrid>().Instance;

        lignes.Preview(Quantite, "12").Should().BeNull();
        lignes.Preview(Quantite, "3h").Should().Be("3 h");
        lignes.Preview(Quantite, "1,5 j").Should().Be("1,5 j");
        lignes.Preview(Prix, "2x45").Should().Be("= 90,00");
        lignes.Preview(Quantite, "3 kg").Should().Be("Saisie non reconnue");
    }

    [Fact]
    public async Task F4_sur_la_designation_propose_celle_du_catalogue_puis_celles_deja_ecrites()
    {
        _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001", Ligne("Tenue comptable d'octobre", _tenue.Id), Ligne("Frais de déplacement"));
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "FAC-2026-0001", Ligne("Tenue comptable (rattrapage)", _tenue.Id));
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Ligne("Tenue", _tenue.Id));
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        await page.InvokeAsync(() => Grille(page).Instance.OnCommand("F4", 0, Designation));

        page.WaitForAssertion(() => page.FindAll("[role=listbox] [role=option]").Select(o => o.TextContent.Trim())
            .Should().StartWith("Tenue comptable mensuelle").And.BeEquivalentTo(["Tenue comptable mensuelle", "Tenue comptable d'octobre", "Tenue comptable (rattrapage)"],
                "la désignation de la ligne elle-même et celles d'autres articles ne sont pas proposées"));
    }

    [Fact]
    public async Task Les_articles_habituels_du_client_s_ajoutent_d_un_clic()
    {
        _quotes.Add(QuoteStatusDto.Accepted, "DEV-2026-0001", Ligne("Tenue comptable mensuelle", _tenue.Id));
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "FAC-2026-0001", Ligne("Tenue comptable mensuelle", _tenue.Id));
        QuoteResponse devis = _quotes.Add(QuoteStatusDto.Draft, null, Ligne("Conseil"));
        IRenderedComponent<QuoteEditor> page = Ouvrir(devis.Id);

        page.WaitForAssertion(() => page.Find("#customer-habits .lt-chip").TextContent.Trim().Should().Be("Ajouter « Tenue comptable mensuelle »"));
        page.Find("#customer-habits .lt-chip").GetAttribute("title").Should().Be("2 pièces");
        page.Find("#customer-habits .lt-chip").Click();

        page.WaitForAssertion(() => page.FindAll(".lg-row[data-r]").Should().HaveCount(2));
        page.WaitForAssertion(() => _quotes.Updates.Last().Request.Lines.Should().Contain(l => l.CatalogItemId == _tenue.Id && l.UnitPriceExclTax == 250m));
        page.FindAll("#customer-habits").Should().BeEmpty("l'article est maintenant sur le devis");
        await Task.CompletedTask;
    }

    [Fact]
    public void Une_facture_sans_conditions_propose_celles_de_la_derniere_facture_du_client()
    {
        _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Issued, "FAC-2026-0001", Ligne("Conseil"));
        InvoiceResponse ancienne = _invoices.Invoices.Values.Single();
        _invoices.Invoices[ancienne.Id] = ancienne with { PaymentTerms = "Paiement à 30 jours fin de mois." };
        InvoiceResponse brouillon = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null, Ligne("Conseil")) with { PaymentTerms = null };
        _invoices.Invoices[brouillon.Id] = brouillon;

        IRenderedComponent<InvoiceEditor> page = Render<InvoiceEditor>(p => p.Add(e => e.Id, brouillon.Id).Add(e => e.AutosaveDelay, TimeSpan.Zero));

        page.WaitForAssertion(() => page.Find("#reuse-terms").TextContent.Trim().Should().Be("Reprendre ses dernières conditions : Paiement à 30 jours fin de mois."));
        page.Find("#reuse-terms").Click();

        page.Find("#payment-terms").GetAttribute("value").Should().Be("Paiement à 30 jours fin de mois.");
        page.FindAll("#reuse-terms").Should().BeEmpty();
    }
}
