using System.Globalization;
using Bunit;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Explorer;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Banking;
using Lettra.UI.Shared.Pages.CatalogItems;
using Lettra.UI.Shared.Pages.Parties;
using Lettra.UI.Shared.Typing;
using Lettra.UI.Tests.Banking;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Consultation;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Lists;

/// <summary>
/// Listes paginées hors pièces (clients, fournisseurs, articles, encaissements) : le tri et les filtres partent au serveur, la liste se relit
/// depuis la première page.
/// </summary>
[Collection("culture")]
public sealed class ServerSideListTests : BunitContext
{
    public ServerSideListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(AssistModule.Path).Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new ExplorerSettings { SearchDelay = TimeSpan.FromMilliseconds(1) });
        Services.AddLettraUi();
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<TPage> Open<TPage>(string route)
        where TPage : IComponent
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        IRenderedComponent<TPage> page = Render<TPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    [Fact]
    public void Les_clients_se_trient_par_ville_et_se_filtrent_par_nature_sur_le_serveur()
    {
        FakeCustomersGateway customers = new();
        customers.Customers.Add(new CustomerSummary(Guid.CreateVersion7(), CustomerKindDto.Company, "Atelier Dupont", "552100554", "Nantes"));
        customers.Customers.Add(new CustomerSummary(Guid.CreateVersion7(), CustomerKindDto.Individual, "Zoé Lambert", null, "Bordeaux"));
        customers.Customers.Add(new CustomerSummary(Guid.CreateVersion7(), CustomerKindDto.Company, "Boulangerie Martin", "732829320", "Paris"));
        Services.AddSingleton<ICustomersGateway>(customers);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        Services.AddSingleton<IAccountingGateway>(new FakeAccountingGateway());
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        IRenderedComponent<CustomersPage> page = Open<CustomersPage>("customers");
        string[] Names() => [.. page.FindAll("#customers-list .lt-record-title").Select(t => t.TextContent)];

        Names().Should().Equal("Atelier Dupont", "Boulangerie Martin", "Zoé Lambert");
        customers.Queries[^1].Should().Be(new PartyListQuery(null, 1, 50));

        page.Find("#customers-sort").Change("City:Descending");
        page.WaitForAssertion(() => Names().Should().Equal("Boulangerie Martin", "Atelier Dupont", "Zoé Lambert"));
        customers.Queries[^1].Should().Match<PartyListQuery>(q => q.Sort == PartySortDto.City && q.Direction == SortDirectionDto.Descending && q.Page == 1);

        page.Find("#customers-kind").Change(nameof(CustomerKindDto.Individual));
        page.WaitForAssertion(() => Names().Should().Equal("Zoé Lambert"));
        customers.Queries[^1].Kind.Should().Be(CustomerKindDto.Individual);
        customers.Queries[^1].Sort.Should().Be(PartySortDto.City, "le tri choisi est gardé");

        customers.Customers.RemoveAll(c => c.Kind == CustomerKindDto.Individual);
        page.Find("#customers-kind").Change(nameof(CustomerKindDto.Individual));
        page.WaitForAssertion(() => page.Find("#customers-empty").TextContent.Should().Contain("Rien ne correspond"));
        page.Find("#customers-empty-reset").Click();
        page.WaitForAssertion(() => Names().Should().HaveCount(2));
        customers.Queries[^1].Kind.Should().BeNull();
    }

    [Fact]
    public void Les_fournisseurs_se_trient_par_ville_sur_le_serveur()
    {
        FakeSuppliersGateway suppliers = new();
        suppliers.Suppliers.Add(new SupplierSummary(Guid.CreateVersion7(), "Papeterie Dupuis", null, "Liège"));
        Services.AddSingleton<ISuppliersGateway>(suppliers);
        Services.AddSingleton<IDirectoryGateway>(new FakeDirectoryGateway());
        IRenderedComponent<SuppliersPage> page = Open<SuppliersPage>("suppliers");

        page.Find("#suppliers-sort").Change("City:Ascending");

        page.WaitForAssertion(() => suppliers.Queries[^1].Should().Be(new PartyListQuery(null, 1, 50, PartySortDto.City)));
        page.Find("#suppliers-sort").GetAttribute("value").Should().Be("City:Ascending");
    }

    [Fact]
    public void Les_articles_se_trient_par_prix_et_la_nature_part_au_serveur()
    {
        FakeCatalogGateway catalog = new();
        catalog.Add("TENUE", "Tenue comptable", 450m);
        catalog.Add("BILAN", "Bilan annuel", 1800m);
        catalog.Add("PAIE", "Bulletin de paie", 25m);
        Services.AddSingleton<ICatalogGateway>(catalog);
        IRenderedComponent<CatalogPage> page = Open<CatalogPage>("catalog");
        string[] Designations() => [.. page.FindAll("#catalog-items-list .lt-record-title").Select(t => t.TextContent)];

        catalog.Queries[^1].Should().Be(new CatalogListQuery(null, false, 1, 50));

        page.Find("#catalog-sort").Change("UnitPrice:Descending");
        page.WaitForAssertion(() => catalog.Queries[^1].Should().Match<CatalogListQuery>(q => q.Sort == CatalogSortDto.UnitPrice && q.Direction == SortDirectionDto.Descending));
        page.FindAll("#catalog-items-list .lt-record").Select(r => r.TextContent).First().Should().Contain("Bilan annuel");

        page.Find("#catalog-kind-good").Click();
        page.WaitForAssertion(() => catalog.Queries[^1].Kind.Should().Be(CatalogItemKindDto.Good));
        catalog.Queries[^1].Sort.Should().Be(CatalogSortDto.UnitPrice, "le tri choisi est gardé");
        page.Find("#catalog-items-empty").Should().NotBeNull("aucun bien dans ce catalogue");
        Designations().Should().BeEmpty();
    }

    [Fact]
    public void Les_encaissements_se_trient_et_se_bornent_sur_le_serveur_depuis_la_premiere_page()
    {
        FakeBankingGateway banking = new();
        Services.AddSingleton<IBankingGateway>(banking);
        IRenderedComponent<PaymentsPage> page = Open<PaymentsPage>("payments");

        banking.PaymentQueries[^1].Should().Be(new PaymentListQuery(PageSize: 50), "la date, la plus récente en tête, par défaut");

        page.Find("#payments-amount-min").Change("85,50");
        page.WaitForAssertion(() => banking.PaymentQueries[^1].MinAmount.Should().Be(85.5m));
        page.Find("#payments-filtered-empty").Should().NotBeNull();
        page.Find("#payments-filters-toggle").TextContent.Should().Contain("1");

        page.Find("#payments-sort").Change("Amount:Ascending");
        page.WaitForAssertion(() => banking.PaymentQueries[^1].Should().Match<PaymentListQuery>(
            q => q.Sort == PaymentSortDto.Amount && q.Direction == SortDirectionDto.Ascending && q.Page == 1 && q.MinAmount == 85.5m));

        page.Find("#payments-empty-reset").Click();
        page.WaitForAssertion(() => banking.PaymentQueries[^1].MinAmount.Should().BeNull());
        banking.PaymentQueries[^1].Sort.Should().Be(PaymentSortDto.Amount, "effacer les filtres garde le tri");
    }
}
