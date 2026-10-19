using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Tri, filtres et bornes des listes : mêmes paramètres et même résultat sur les deux transports.</summary>
public sealed partial class GatewayParityTests
{
    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Lister_les_pieces_triees_et_bornees_par_montant(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        foreach (decimal price in new[] { 100m, 1000m, 10.5m })
        {
            DocumentLineRequest line = new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, UnitCode = "HUR", Kind = CatalogItemKindDto.Service };
            (await invoices.CreateAsync(new SaveInvoiceRequest { CustomerId = customer, Lines = [line] }, default)).IsSuccess.Should().BeTrue();
            (await quotes.CreateAsync(new SaveQuoteRequest { CustomerId = customer, Lines = [line] }, default)).IsSuccess.Should().BeTrue();
        }

        OperationResult<PagedResponse<InvoiceSummary>> ascending = await invoices.ListAsync(
            new BillingListQuery(CustomerId: customer, Sort: BillingSortDto.TotalInclTax, Direction: SortDirectionDto.Ascending), null, null, default);
        OperationResult<PagedResponse<InvoiceSummary>> bounded = await invoices.ListAsync(
            new BillingListQuery(CustomerId: customer, Sort: BillingSortDto.TotalExclTax, Direction: SortDirectionDto.Descending, MinAmount: 12.61m, MaxAmount: 1200m),
            null, null, default);
        OperationResult<PagedResponse<QuoteSummary>> quoteList = await quotes.ListAsync(
            new BillingListQuery(CustomerId: customer, Sort: BillingSortDto.TotalInclTax, Direction: SortDirectionDto.Descending, MaxAmount: 120m), null, default);
        OperationResult<PagedResponse<InvoiceSummary>> reversed = await invoices.ListAsync(
            new BillingListQuery(CustomerId: customer, MinAmount: 500m, MaxAmount: 100m), null, null, default);

        ascending.Value!.Items.Select(i => i.TotalInclTax).Should().Equal(12.6m, 120m, 1200m);
        bounded.Value!.Items.Select(i => i.TotalExclTax).Should().Equal(1000m, 100m);
        quoteList.Value!.Items.Select(q => q.TotalInclTax).Should().Equal(120m, 12.6m);
        reversed.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Validation.Invalid, "MaxAmount"));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Lister_les_clients_et_fournisseurs_tries_par_ville_et_filtres_par_nature(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        ICustomersGateway customers = services.GetRequiredService<ICustomersGateway>();
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        (await customers.CreateAsync(CustomerApiTests.Martin(), default)).IsSuccess.Should().BeTrue();
        (await customers.CreateAsync(new CreateCustomerRequest
        {
            Kind = CustomerKindDto.Individual, Name = "Zoé Lambert", AddressLine1 = "2 quai des Chartrons", PostalCode = "33000", City = "Bordeaux",
        }, default)).IsSuccess.Should().BeTrue();
        (await suppliers.CreateAsync(PartiesAndCatalogApiTests.Dupuis(), default)).IsSuccess.Should().BeTrue();

        OperationResult<PagedResponse<CustomerSummary>> byCity = await customers.SearchAsync(
            new PartyListQuery(Sort: PartySortDto.City, Direction: SortDirectionDto.Descending), default);
        OperationResult<PagedResponse<CustomerSummary>> individuals = await customers.SearchAsync(new PartyListQuery(Kind: CustomerKindDto.Individual), default);
        OperationResult<PagedResponse<Contracts.Suppliers.SupplierSummary>> supplierList = await suppliers.SearchAsync(
            new PartyListQuery("dupuis", Sort: PartySortDto.City), default);
        OperationResult<PagedResponse<CustomerSummary>> refused = await customers.SearchAsync(new PartyListQuery(Sort: (PartySortDto)9), default);

        // Le transport en direct partage sa base entre les tests : on vérifie l'ordre et le filtre, pas un compte exact.
        byCity.Value!.Items.Select(c => c.City).Should().StartWith("Paris").And.EndWith("Bordeaux");
        individuals.Value!.Items.Should().OnlyContain(c => c.Kind == CustomerKindDto.Individual).And.Contain(c => c.Name == "Zoé Lambert");
        individuals.Value.TotalCount.Should().Be(individuals.Value.Items.Count);
        supplierList.Value!.Items.Should().NotBeEmpty().And.OnlyContain(s => s.Name == "Minoterie Dupuis");
        refused.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Validation.Invalid, "Sort"));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Lister_les_articles_tries_par_prix_et_filtres_par_nature(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        ICatalogGateway catalog = services.GetRequiredService<ICatalogGateway>();
        string prefix = "TRI" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(prefix + "-A") with { UnitPriceExclTax = 300m }, default)).IsSuccess.Should().BeTrue();
        (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(prefix + "-B") with { UnitPriceExclTax = 20.5m }, default)).IsSuccess.Should().BeTrue();
        (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(prefix + "-C") with
        {
            Designation = "Classeur", Kind = CatalogItemKindDto.Good, UnitCode = "C62", UnitPriceExclTax = 4m,
        }, default)).IsSuccess.Should().BeTrue();

        OperationResult<PagedResponse<CatalogItemSummary>> serviceItems = await catalog.ListAsync(
            new CatalogListQuery(prefix, Sort: CatalogSortDto.UnitPrice, Direction: SortDirectionDto.Descending, Kind: CatalogItemKindDto.Service), default);
        OperationResult<PagedResponse<CatalogItemSummary>> refused = await catalog.ListAsync(new CatalogListQuery(Kind: (CatalogItemKindDto)9), default);

        serviceItems.Value!.Items.Select(i => i.Reference).Should().Equal(prefix + "-A", prefix + "-B");
        refused.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Validation.Invalid, "Kind"));
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Lister_les_encaissements_tries_par_montant_et_bornes(string transport)
    {
        await using ServiceProvider services = Gateway(transport);
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        IBankingGateway banking = services.GetRequiredService<IBankingGateway>();
        Guid customer = (await services.GetRequiredService<ICustomersGateway>().CreateAsync(CustomerApiTests.Martin(), default)).Value!.Id;
        await SaveCompanyProfileAsync(services);
        InvoiceResponse draft = (await invoices.CreateAsync(
            new SaveInvoiceRequest
            {
                CustomerId = customer,
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 10, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            default)).Value!;
        InvoiceResponse invoice = (await invoices.ValidateAsync(draft.Id, draft.Version, default)).Value!;
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (decimal amount in new[] { 40m, 300m, 85.5m })
        {
            OperationResult<PaymentResponse> recorded = await banking.RecordPaymentAsync(
                new RecordPaymentRequest
                {
                    CustomerId = customer, Date = today, Amount = amount, Method = PaymentMethodDto.Transfer, Allocations = [new PaymentAllocationRequest(invoice.Id, amount)],
                },
                default);
            recorded.IsSuccess.Should().BeTrue(string.Join(", ", recorded.Errors.Select(e => e.Code)));
        }

        OperationResult<PagedResponse<PaymentSummary>> ascending = await banking.ListPaymentsAsync(
            new PaymentListQuery(CustomerId: customer, Sort: PaymentSortDto.Amount, Direction: SortDirectionDto.Ascending), default);
        OperationResult<PagedResponse<PaymentSummary>> bounded = await banking.ListPaymentsAsync(
            new PaymentListQuery(CustomerId: customer, MinAmount: 85.5m, MaxAmount: 300m, Sort: PaymentSortDto.Amount), default);
        OperationResult<PagedResponse<PaymentSummary>> byNumber = await banking.ListPaymentsAsync(
            new PaymentListQuery(CustomerId: customer, Sort: PaymentSortDto.Number, Direction: SortDirectionDto.Ascending), default);
        OperationResult<PagedResponse<PaymentSummary>> byCustomer = await banking.ListPaymentsAsync(
            new PaymentListQuery(CustomerId: customer, Sort: PaymentSortDto.Customer, Direction: SortDirectionDto.Ascending), default);
        OperationResult<PagedResponse<PaymentSummary>> reversed = await banking.ListPaymentsAsync(new PaymentListQuery(MinAmount: 10m, MaxAmount: 1m), default);

        ascending.Value!.Items.Select(p => p.Amount).Should().Equal(40m, 85.5m, 300m);
        bounded.Value!.Items.Select(p => p.Amount).Should().Equal(300m, 85.5m);
        bounded.Value.TotalCount.Should().Be(2);
        byNumber.Value!.Items.Select(p => p.Number).Should().BeInAscendingOrder(StringComparer.Ordinal).And.HaveCount(3);
        byCustomer.Value!.Items.Select(p => p.Number).Should().BeInDescendingOrder(StringComparer.Ordinal, "à client égal, le numéro le plus récent d'abord");
        reversed.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.Validation.Invalid, "MaxAmount"));
    }
}
