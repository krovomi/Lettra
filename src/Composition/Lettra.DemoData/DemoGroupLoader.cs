using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;
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

    /// <summary>
    /// Cherche parmi <paramref name="dossiers"/> le prestataire et le négoce du jeu (par le SIREN de leur fiche) ; une
    /// fiche changée à la main, ou un dossier absent, et rien n'est fait.
    /// </summary>
    public async Task LoadAsync(IEnumerable<Guid> dossiers, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossiers);
        string sellerSiren = FrenchIdentifiers.Siren(DemoCompanies.For(DemoBusiness.Services).SirenOrdinal);
        string buyerSiren = FrenchIdentifiers.Siren(DemoCompanies.For(DemoBusiness.Trading).SirenOrdinal);
        (Guid Dossier, CompanyProfileResponse Profile)? sold = null;
        (Guid Dossier, CompanyProfileResponse Profile)? bought = null;
        foreach (Guid dossier in dossiers.Distinct())
        {
            CompanyProfileResponse? profile = await ProfileAsync(dossier, cancellationToken).ConfigureAwait(false);
            if (profile?.Siren == sellerSiren)
            {
                sold = (dossier, profile);
            }
            else if (profile?.Siren == buyerSiren)
            {
                bought = (dossier, profile);
            }
        }

        if (sold is not (Guid seller, { Iban: { } sellerIban } soldProfile) || bought is not (Guid buyer, { Iban: { } buyerIban } boughtProfile))
        {
            return;
        }

        await LoadAsync(seller, soldProfile, sellerIban, buyer, boughtProfile, buyerIban, today, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadAsync(
        Guid seller, CompanyProfileResponse sold, string sellerIban, Guid buyer, CompanyProfileResponse bought, string buyerIban, DateOnly today, CancellationToken cancellationToken)
    {
        string sellerSiren = sold.Siren!;
        string buyerSiren = bought.Siren!;
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
        (decimal Amount, string Label)[] transfers =
        [
            (issued[0].Totals.TotalInclTax, $"VIR SEPA {payee} FACTURE {issued[0].Number}"),
            (issued[2].Totals.TotalInclTax, $"VIR SEPA {payee} PRESTATIONS"),
        ];
        DemoDossierLoader.Ok(await _banking.ImportStatementAsync(
            new ImportBankStatementRequest
            {
                FileName = $"releve-groupe-{today:yyyy-MM-dd}.xml",
                ContentBase64 = Convert.ToBase64String(Transfers(buyerIban, sold.Name, sellerIban, transfers, today)),
            },
            cancellationToken).ConfigureAwait(false), "relevé du groupe");
    }

    private async Task<CompanyProfileResponse?> ProfileAsync(Guid dossier, CancellationToken cancellationToken)
    {
        session.CurrentDossierId = dossier;
        return (await _settings.GetCompanyProfileAsync(cancellationToken).ConfigureAwait(false)).Value;
    }

    private static readonly XNamespace Camt = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";

    /// <summary>Un relevé CAMT.053 du compte de l'acheteur : des virements du jour vers l'IBAN du vendeur.</summary>
    private static byte[] Transfers(string account, string payee, string payeeIban, (decimal Amount, string Label)[] transfers, DateOnly today)
    {
        string day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        XElement statement = new(
            Camt + "Stmt",
            new XElement(Camt + "Id", $"GROUPE-{today:yyyyMMdd}"),
            new XElement(Camt + "CreDtTm", day + "T07:00:00"),
            new XElement(Camt + "Acct", new XElement(Camt + "Id", new XElement(Camt + "IBAN", account)), new XElement(Camt + "Ccy", "EUR")),
            transfers.Select((t, i) => new XElement(
                Camt + "Ntry",
                new XElement(Camt + "Amt", new XAttribute("Ccy", "EUR"), t.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
                new XElement(Camt + "CdtDbtInd", "DBIT"),
                new XElement(Camt + "Sts", new XElement(Camt + "Cd", "BOOK")),
                new XElement(Camt + "BookgDt", new XElement(Camt + "Dt", day)),
                new XElement(Camt + "AcctSvcrRef", $"GROUPE{today:yyyyMMdd}{i + 1:D4}"),
                new XElement(Camt + "NtryDtls", new XElement(Camt + "TxDtls",
                    new XElement(Camt + "RltdPties",
                        new XElement(Camt + "Cdtr", new XElement(Camt + "Pty", new XElement(Camt + "Nm", payee))),
                        new XElement(Camt + "CdtrAcct", new XElement(Camt + "Id", new XElement(Camt + "IBAN", payeeIban)))),
                    new XElement(Camt + "RmtInf", new XElement(Camt + "Ustrd", t.Label)))))));
        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Camt + "Document", new XElement(Camt + "BkToCstmrStmt",
                new XElement(Camt + "GrpHdr", new XElement(Camt + "MsgId", $"LETTRA-GROUPE-{today:yyyyMMdd}"), new XElement(Camt + "CreDtTm", day + "T07:00:00")),
                statement)));
        return Encoding.UTF8.GetBytes(document.Declaration + document.ToString(SaveOptions.DisableFormatting));
    }
}
