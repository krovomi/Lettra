using Lettra.Application.Abstractions.Identity;
using Lettra.Bootstrap;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Exports;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Le parcours « Premier usage » du README, avec ses données fictives, sur la composition de l'application de bureau
/// (poste isolé, gateways in-process, plateforme agréée simulée) : si ce test casse, le README ment.
/// </summary>
public sealed class DemoWalkthroughTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-demo-").FullName;

    [Fact]
    public async Task Le_parcours_du_README_va_du_compte_du_poste_a_l_encaissement()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Comme MauiProgram : poste isolé et plateforme simulée.
            [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
            [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(_folder, "dossiers"),
            ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
            ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
            ["Lettra:EInvoicing:Platform"] = "Simulated",
        }).Build();
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddLettra(configuration);
        collection.AddSingleton<IKekStore, InMemoryKekStore>();
        collection.AddSingleton<IDossierSelection>(sp => new ActiveDossier(sp.GetRequiredService<LocalDossierRegistry>()));
        collection.AddInProcessGateways();
        await using ServiceProvider services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Démarrage de l'application (avant tout compte), puis 1. « Créer le compte de ce poste ».
        await services.InitializeLettraAsync(configuration, CancellationToken.None);
        (await services.GetRequiredService<ILocalAccounts>().CreateAsync("Camille Démo", "atelier des comptes 2026", default)).IsSuccess.Should().BeTrue();

        // 2. « Entreprise » : la fiche du cabinet de démonstration.
        OperationResult<CompanyProfileResponse> company = await services.GetRequiredService<IBillingSettingsGateway>().SaveCompanyProfileAsync(
            new SaveCompanyProfileRequest
            {
                Name = "Cabinet Démo Conseil", LegalForm = "SAS", ShareCapital = 10_000m, Siren = "900000019",
                Rcs = "RCS Paris 900 000 019", VatNumber = "FR25900000019",
                AddressLine1 = "12 rue des Exemples", PostalCode = "75010", City = "Paris",
                LatePenaltyRate = 10m, EarlyPaymentDiscount = "Pas d'escompte pour paiement anticipé",
            },
            null,
            default);
        company.IsSuccess.Should().BeTrue(Codes(company.Errors));

        // 3. « Clients » : un client fictif saisi à la main.
        OperationResult<CustomerResponse> customer = await services.GetRequiredService<ICustomersGateway>().CreateAsync(
            new CreateCustomerRequest
            {
                Kind = CustomerKindDto.Company, Name = "Boulangerie Exemple", Siret = "90000002700012",
                AddressLine1 = "3 place du Marché", PostalCode = "69002", City = "Lyon",
            },
            default);
        customer.IsSuccess.Should().BeTrue(Codes(customer.Errors));

        // 4. « Devis » : nouveau devis, validé, accepté, puis « Facturer ».
        IQuotesGateway quotes = services.GetRequiredService<IQuotesGateway>();
        QuoteResponse draftQuote = (await quotes.CreateAsync(
            new SaveQuoteRequest
            {
                CustomerId = customer.Value!.Id,
                Subject = "Accompagnement comptable",
                Lines = [new DocumentLineRequest { Designation = "Tenue comptable mensuelle", Quantity = 3, UnitPriceExclTax = 250m, UnitCode = "C62", Kind = CatalogItemKindDto.Service }],
            },
            default)).Value!;
        OperationResult<QuoteResponse> validatedQuote = await quotes.ValidateAsync(draftQuote.Id, draftQuote.Version, default);
        validatedQuote.IsSuccess.Should().BeTrue(Codes(validatedQuote.Errors));
        OperationResult<QuoteResponse> accepted = await quotes.SetOutcomeAsync(draftQuote.Id, accepted: true, validatedQuote.Value!.Version, default);
        accepted.IsSuccess.Should().BeTrue(Codes(accepted.Errors));
        OperationResult<InvoiceResponse> fromQuote = await quotes.CreateInvoiceAsync(draftQuote.Id, default);
        fromQuote.IsSuccess.Should().BeTrue(Codes(fromQuote.Errors));

        // 5. « Factures » : la facture issue du devis, validée (numéro FAC-…), puis le XML du Factur-X (CII) et l'UBL.
        IInvoicesGateway invoices = services.GetRequiredService<IInvoicesGateway>();
        OperationResult<InvoiceResponse> invoice = await invoices.ValidateAsync(fromQuote.Value!.Id, fromQuote.Value.Version, default);
        invoice.IsSuccess.Should().BeTrue(Codes(invoice.Errors));
        invoice.Value!.Number.Should().StartWith("FAC-");
        invoice.Value.Totals.TotalInclTax.Should().Be(900m);
        (await invoices.GetEInvoiceAsync(invoice.Value.Id, EInvoiceFormatDto.Cii, default)).IsSuccess.Should().BeTrue();
        (await invoices.GetEInvoiceAsync(invoice.Value.Id, EInvoiceFormatDto.Ubl, default)).IsSuccess.Should().BeTrue();

        // 6. « Plateforme agréée » : transmission, statuts, encaissement.
        IPlatformTransmissionGateway platform = services.GetRequiredService<IPlatformTransmissionGateway>();
        OperationResult<PlatformTransmissionResponse> sent = await platform.TransmitAsync(invoice.Value.Id, new TransmitInvoiceRequest(), default);
        sent.IsSuccess.Should().BeTrue(Codes(sent.Errors));
        OperationResult<PlatformTransmissionResponse> refreshed = await platform.RefreshAsync(invoice.Value.Id, default);
        refreshed.Value!.History.Should().Contain(h => h.Status == PlatformStatusDto.MadeAvailable);
        OperationResult<PlatformTransmissionResponse> paid = await platform.ReportPaymentAsync(
            invoice.Value.Id, new ReportPaymentRequest { Date = invoice.Value.IssueDate!.Value, Amount = 900m }, default);
        paid.IsSuccess.Should().BeTrue(Codes(paid.Errors));
        paid.Value!.CollectedAmount.Should().Be(900m);

        // 7. « Avoir » sur la facture émise, comme l'écran (motif seul : avoir total), puis validé comme une facture.
        OperationResult<InvoiceResponse> credit = await invoices.CreateCreditNoteAsync(
            invoice.Value.Id, new CreateCreditNoteRequest { Reason = "Geste commercial" }, default);
        credit.IsSuccess.Should().BeTrue(Codes(credit.Errors));
        credit.Value!.Kind.Should().Be(InvoiceKindDto.CreditNote);
        OperationResult<InvoiceResponse> creditValidated = await invoices.ValidateAsync(credit.Value.Id, credit.Value.Version, default);
        creditValidated.IsSuccess.Should().BeTrue(Codes(creditValidated.Errors));
        creditValidated.Value!.Number.Should().NotBeNullOrEmpty();

        // 8. « FEC » : l'exercice civil en cours, exporté puis passé au validateur intégré.
        IFecGateway fec = services.GetRequiredService<IFecGateway>();
        int year = invoice.Value.IssueDate!.Value.Year;
        OperationResult<FecDocument> export = await fec.ExportAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), default);
        export.IsSuccess.Should().BeTrue(Codes(export.Errors));
        export.Value!.FileName.Should().Be($"900000019FEC{year}1231.txt");
        OperationResult<FecValidationResponse> report = await fec.ValidateAsync(export.Value.FileName, export.Value.Content, null, null, default);
        report.IsSuccess.Should().BeTrue(Codes(report.Errors));
        report.Value!.IsValid.Should().BeTrue();
        report.Value.EntryCount.Should().BeGreaterThanOrEqualTo(2, "la facture et l'avoir");
        report.Value.TotalDebit.Should().Be(report.Value.TotalCredit);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private static string Codes(IEnumerable<ErrorDetail> errors) => string.Join(", ", errors.Select(e => e.Code));

    /// <summary>Comme l'application de bureau : le dossier ouvert est celui du registre local.</summary>
    private sealed class ActiveDossier(LocalDossierRegistry registry) : IDossierSelection
    {
        public Guid? CurrentDossierId => registry.ActiveDossierId;
    }
}
