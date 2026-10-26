using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
using Lettra.Exports.Application.Banking;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.DemoData;

/// <summary>
/// Les comptes intra-groupe de la démonstration : l'entreprise de services facture au négoce l'informatique du groupe.
/// Trois factures du jour ; le vendeur en a encaissé deux. Chez l'acheteur, la fiche fournisseur du vendeur porte un
/// autre IBAN que le sien, et sa banque montre deux virements vers le vendeur : l'un cite la première facture, l'autre
/// paie la troisième sans la citer. L'écran Comptes intra-groupe a ainsi chaque écart à montrer. Fait une seule fois :
/// rien n'est refait quand le vendeur a déjà l'acheteur pour client.
/// </summary>
internal sealed class DemoGroupLoader(IServiceProvider services, DemoSession session)
{
    private readonly IBillingSettingsGateway _settings = services.GetRequiredService<IBillingSettingsGateway>();
    private readonly ICustomersGateway _customers = services.GetRequiredService<ICustomersGateway>();
    private readonly ISuppliersGateway _suppliers = services.GetRequiredService<ISuppliersGateway>();
    private readonly IInvoicesGateway _invoices = services.GetRequiredService<IInvoicesGateway>();
    private readonly IBankingGateway _banking = services.GetRequiredService<IBankingGateway>();

    /// <summary>Heures facturées et taux horaire HT des trois factures.</summary>
    private static readonly (string Subject, decimal Hours, decimal Rate)[] Invoices =
    [
        ("Infogérance du parc informatique du groupe", 12m, 125m),
        ("Assistance aux utilisateurs du groupe", 8m, 100m),
        ("Migration de la messagerie du groupe", 20m, 100m),
    ];

    public async Task LoadAsync(Guid seller, Guid buyer, DateOnly today, CancellationToken cancellationToken)
    {
        CompanyProfileResponse? sold = await ProfileAsync(seller, cancellationToken).ConfigureAwait(false);
        CompanyProfileResponse? bought = await ProfileAsync(buyer, cancellationToken).ConfigureAwait(false);
        if (sold is not { Siren: { } sellerSiren, Iban: { } sellerIban } || bought is not { Siren: { } buyerSiren, Iban: { } buyerIban })
        {
            return;
        }

        session.CurrentDossierId = seller;
        PagedResponse<CustomerSummary> known = DemoDossierLoader.Ok(await _customers.SearchAsync(new PartyListQuery(bought.Name, PageSize: 20), cancellationToken).ConfigureAwait(false), "clients");
        if (known.Items.Any(c => c.Siren == buyerSiren))
        {
            return;
        }

        Guid customer = DemoDossierLoader.Ok(await _customers.CreateAsync(
            new CreateCustomerRequest
            {
                Kind = CustomerKindDto.Company,
                Name = bought.Name,
                Siren = buyerSiren,
                VatNumber = bought.VatNumber,
                Email = "comptabilite@groupe.example",
                AddressLine1 = bought.AddressLine1,
                PostalCode = bought.PostalCode,
                City = bought.City,
            },
            cancellationToken).ConfigureAwait(false), "client du groupe").Id;
        List<InvoiceResponse> issued = [];
        foreach ((string subject, decimal hours, decimal rate) in Invoices)
        {
            InvoiceResponse draft = DemoDossierLoader.Ok(await _invoices.CreateAsync(
                new SaveInvoiceRequest
                {
                    CustomerId = customer,
                    Subject = subject,
                    Lines = [new() { Designation = subject, Quantity = hours, UnitPriceExclTax = rate, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
                },
                cancellationToken).ConfigureAwait(false), "facture du groupe");
            issued.Add(DemoDossierLoader.Ok(await _invoices.ValidateAsync(draft.Id, draft.Version, cancellationToken).ConfigureAwait(false), "validation d'une facture du groupe"));
        }

        foreach (InvoiceResponse paid in issued.Take(2))
        {
            DemoDossierLoader.Ok(await _banking.RecordPaymentAsync(
                new RecordPaymentRequest
                {
                    CustomerId = customer,
                    Date = today,
                    Amount = paid.Totals.TotalInclTax,
                    Reference = paid.Number,
                    Allocations = [new PaymentAllocationRequest(paid.Id, paid.Totals.TotalInclTax)],
                },
                cancellationToken).ConfigureAwait(false), "encaissement du groupe");
        }

        session.CurrentDossierId = buyer;
        DemoDossierLoader.Ok(await _suppliers.CreateAsync(
            new CreateSupplierRequest
            {
                Name = sold.Name,
                Siren = sellerSiren,
                VatNumber = sold.VatNumber,
                Email = "factures@groupe.example",
                Iban = FrenchIdentifiers.Iban(7, 30_001, 4_400_000_017L),
                AddressLine1 = sold.AddressLine1,
                PostalCode = sold.PostalCode,
                City = sold.City,
            },
            cancellationToken).ConfigureAwait(false), "fournisseur du groupe");
        string payee = sold.Name.ToUpperInvariant();
        DemoBankEntry[] transfers =
        [
            new(today, -issued[0].Totals.TotalInclTax, $"VIR SEPA {payee} FACTURE {issued[0].Number}", sold.Name, sellerIban),
            new(today, -issued[2].Totals.TotalInclTax, $"VIR SEPA {payee} PRESTATIONS", sold.Name, sellerIban),
        ];
        DemoDossierLoader.Ok(await _banking.ImportStatementAsync(
            new ImportBankStatementRequest
            {
                FileName = $"releve-groupe-{today:yyyy-MM-dd}.xml",
                ContentBase64 = Convert.ToBase64String(DemoBankStatement.Statement(buyerIban, $"GROUPE-{today:yyyyMMdd}", transfers, today)),
            },
            cancellationToken).ConfigureAwait(false), "relevé du groupe");
    }

    private async Task<CompanyProfileResponse?> ProfileAsync(Guid dossier, CancellationToken cancellationToken)
    {
        session.CurrentDossierId = dossier;
        return (await _settings.GetCompanyProfileAsync(cancellationToken).ConfigureAwait(false)).Value;
    }
}
