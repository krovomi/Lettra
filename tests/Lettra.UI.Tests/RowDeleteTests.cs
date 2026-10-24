using System.Globalization;
using Bunit;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Billing;
using Lettra.UI.Shared.Pages.CatalogItems;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Consultation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

/// <summary>
/// Corbeille en bout de ligne : un clic, une confirmation dans la ligne, le toaster. Les pièces numérotées gardent leur
/// corbeille, qui explique ce qu'il faut faire à la place ; un article est archivé, un tiers retiré des listes.
/// </summary>
[Collection("culture")]
public sealed class RowDeleteTests : BunitContext
{
    private readonly FakeInvoicesGateway _invoices = new();
    private readonly FakeQuotesGateway _quotes = new();
    private readonly FakeCustomersGateway _customers = new();
    private readonly FakeSuppliersGateway _suppliers = new();
    private readonly FakeCatalogGateway _catalog = new();

    public RowDeleteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new ExplorerSettings { SearchDelay = TimeSpan.FromMilliseconds(1) });
        Services.AddLettraUi();
        Services.AddSingleton<IInvoicesGateway>(_invoices);
        Services.AddSingleton<IQuotesGateway>(_quotes);
        Services.AddSingleton<ICustomersGateway>(_customers);
        Services.AddSingleton<ISuppliersGateway>(_suppliers);
        Services.AddSingleton<ICatalogGateway>(_catalog);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        Services.AddSingleton<IAccountingGateway>(new FakeAccountingGateway());
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<TPage> Open<TPage>(string route)
        where TPage : IComponent
    {
        Navigation.NavigateTo(route);
        IRenderedComponent<TPage> page = Render<TPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Un_devis_brouillon_se_supprime_depuis_la_liste_apres_confirmation()
    {
        QuoteResponse brouillon = _quotes.Add();
        _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001");
        IRenderedComponent<QuoteList> page = Render<QuoteList>();
        page.FindAll("tbody tr").Should().HaveCount(2);

        page.Find($"tbody tr:has(a[href='quotes/{brouillon.Id}']) .lt-row-delete").Click();

        page.Find(".lt-row-confirm-text").TextContent.Should().Be("Supprimer cette ligne ?");
        _quotes.Quotes.Should().ContainKey(brouillon.Id, "rien n'est supprimé avant la confirmation");
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle());
        _quotes.Quotes.Should().NotContainKey(brouillon.Id);
        this.ToastText().Should().Be("Brouillon supprimé.");
    }

    [Fact]
    public void Annuler_ou_Echap_garde_la_ligne()
    {
        _quotes.Add();
        IRenderedComponent<QuoteList> page = Render<QuoteList>();

        page.Find(".lt-row-delete").Click();
        page.Find(".lt-row-confirm-no").Click();
        page.Find(".lt-row-delete").Click();
        page.Find(".lt-row-confirm").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        page.FindAll(".lt-row-confirm").Should().BeEmpty();
        _quotes.Quotes.Should().ContainSingle();
    }

    [Fact]
    public void Un_devis_valide_ne_se_supprime_pas_et_le_toaster_dit_quoi_faire()
    {
        _quotes.Add(QuoteStatusDto.Validated, "DEV-2026-0001");
        IRenderedComponent<QuoteList> page = Render<QuoteList>();

        page.Find(".lt-row-delete").ClassList.Should().Contain("is-blocked");
        page.Find(".lt-row-delete").Click();

        page.FindAll(".lt-row-confirm").Should().BeEmpty();
        _quotes.Quotes.Should().ContainSingle();
        Toast toast = this.Toasts().Should().ContainSingle().Subject;
        toast.Kind.Should().Be(ToastKind.Info);
        toast.Message.Should().Contain("refusé");
    }

    [Fact]
    public void Une_facture_validee_renvoie_vers_l_avoir_et_un_brouillon_se_supprime()
    {
        InvoiceResponse validee = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-2026-0042");
        InvoiceResponse brouillon = _invoices.Add(InvoiceKindDto.Invoice, InvoiceStatusDto.Draft, null);
        IRenderedComponent<InvoiceList> page = Render<InvoiceList>();

        page.Find($"tbody tr:has(a[href='invoices/{validee.Id}']) .lt-row-delete").Click();
        this.ToastText().Should().Contain("avoir");

        page.Find($"tbody tr:has(a[href='invoices/{brouillon.Id}']) .lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().ContainSingle());
        _invoices.Invoices.Keys.Should().Equal(validee.Id);
    }

    [Fact]
    public void Supprimer_un_client_le_retire_de_la_liste_et_le_toaster_rassure_sur_ses_pieces()
    {
        Guid dupont = Guid.CreateVersion7();
        _customers.Customers.Add(new CustomerSummary(dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        _customers.Customers.Add(new CustomerSummary(Guid.CreateVersion7(), CustomerKindDto.Company, "Martin SA", "732829320", "Paris"));
        IRenderedComponent<CustomersPage> page = Open<CustomersPage>("customers");

        page.Find($"#customers-option-{dupont} .lt-row-delete").GetAttribute("title").Should().Be("Supprimer le client Dupont SARL");
        page.FindAll("#customers-list button, #customers-list [tabindex]").Should().BeEmpty("une option de liste ne contient rien de focalisable");
        page.Find($"#customers-option-{dupont}").GetAttribute("aria-keyshortcuts").Should().Be("Delete");
        page.Find($"#customers-option-{dupont} .lt-row-delete").Click();
        page.Find(".lt-row-confirm-text").TextContent.Should().Be("Supprimer ce client ? Entrée pour confirmer, Échap pour annuler.");
        page.FindAll("#customers-list button").Should().BeEmpty("la confirmation non plus");
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.FindAll(".lt-record").Should().ContainSingle());
        _customers.Deletes.Should().Equal((dupont, null));
        page.Find("#customers-count").TextContent.Should().Contain("1");
        this.ToastText().Should().Be("Client « Dupont SARL » supprimé. Ses devis et factures restent consultables.");
        Navigation.Uri.Should().EndWith("/customers", "le clic sur la corbeille n'ouvre pas la fiche");
    }

    [Fact]
    public void Supprimer_le_client_ouvert_envoie_sa_version_et_revient_a_la_liste()
    {
        Guid dupont = Guid.CreateVersion7();
        _customers.Customers.Add(new CustomerSummary(dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        IRenderedComponent<CustomersPage> page = Open<CustomersPage>($"customers/{dupont}");
        page.WaitForAssertion(() => page.Find("#customer-sheet-title").TextContent.Should().Be("Dupont SARL"));

        page.Find("#customers-list").KeyDown(new KeyboardEventArgs { Key = "Delete" });
        page.WaitForAssertion(() => page.Find(".lt-row-confirm").Should().NotBeNull());
        page.Find("#customers-list").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        page.WaitForAssertion(() => _customers.Deletes.Should().Equal((dupont, 1)));
        page.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/customers"));
    }

    [Fact]
    public void Un_refus_du_serveur_garde_le_client_et_passe_par_le_toaster()
    {
        Guid dupont = Guid.CreateVersion7();
        _customers.Customers.Add(new CustomerSummary(dupont, CustomerKindDto.Company, "Dupont SARL", "732829320", "Lyon"));
        _customers.NextErrors = [new Lettra.Contracts.Common.ErrorDetail(Lettra.Contracts.Common.ErrorCodes.Security.Forbidden)];
        IRenderedComponent<CustomersPage> page = Open<CustomersPage>("customers");

        page.Find(".lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
        page.FindAll(".lt-record").Should().ContainSingle();
    }

    [Fact]
    public void Supprimer_un_fournisseur_le_retire_de_la_liste()
    {
        Lettra.Contracts.Suppliers.SupplierResponse papeterie = _suppliers.Add("Papeterie Lumière", "Lyon");
        _suppliers.Add("Imprimerie Martin", "Paris");
        IRenderedComponent<SuppliersPage> page = Open<SuppliersPage>("suppliers");

        page.Find($"#suppliers-option-{papeterie.Id} .lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => page.FindAll(".lt-record").Should().ContainSingle());
        _suppliers.Records[papeterie.Id].IsArchived.Should().BeTrue();
        this.ToastText().Should().Be("Fournisseur « Papeterie Lumière » supprimé.");
    }

    [Fact]
    public void La_corbeille_du_catalogue_archive_l_article_et_la_fleche_le_reactive()
    {
        CatalogItemResponse tenue = _catalog.Add("TENUE", "Tenue comptable", 450m);
        IRenderedComponent<CatalogPage> page = Open<CatalogPage>("catalog");

        page.Find($"#catalog-option-{tenue.Id} .lt-row-delete").Click();
        page.Find(".lt-row-confirm-yes").TextContent.Should().Be("Archiver");
        page.Find(".lt-row-confirm-yes").Click();

        page.WaitForAssertion(() => _catalog.ArchiveCalls.Should().Equal((tenue.Id, true)));
        this.ToastText().Should().StartWith("Article « Tenue comptable » archivé");

        page.Find("#catalog-items-archived").Change(true);
        page.WaitForAssertion(() => page.Find($"#catalog-option-{tenue.Id} .lt-row-delete").GetAttribute("title")
            .Should().Be("Réactiver l'article Tenue comptable"));
        page.Find($"#catalog-option-{tenue.Id} .lt-row-delete").Click();

        page.WaitForAssertion(() => _catalog.ArchiveCalls.Should().Equal((tenue.Id, true), (tenue.Id, false)));
    }
}
