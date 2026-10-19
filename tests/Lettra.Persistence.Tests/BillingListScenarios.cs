using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Tri et bornes de montant des listes de devis et de factures : même résultat sur les trois providers.</summary>
internal static class BillingListScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly Customer.Draft Atelier = new(
        CustomerKind.Company, "Atelier Dupont", "552100554", "55210055400013", null, "contact@dupont.fr",
        "2 rue du Port", null, "44000", "Nantes", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealSimplified);

    private static DocumentLineRequest Line(decimal price) =>
        new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    /// <summary>
    /// Trois factures (60, 360 et 1 440 € TTC, deux clients, une restée en brouillon) et trois devis : chaque colonne se
    /// trie dans les deux sens, les bornes de montant sont comprises, et un tri à égalité garde un ordre stable.
    /// </summary>
    public static async Task SortAndAmountRangeAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Guid martin = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            Guid atelier = (await commands.SendAsync(new CreateCustomerCommand(dossier, Atelier), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            foreach ((Guid customer, decimal price, bool validate) in new[] { (martin, 300m, true), (atelier, 1200m, true), (martin, 50m, false) })
            {
                InvoiceId invoice = (await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [Line(price)] }), default)).Value;
                if (validate)
                {
                    (await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoice, null), default)).IsSuccess.Should().BeTrue();
                }

                (await commands.SendAsync(new CreateQuoteCommand(dossier, new SaveQuoteRequest { CustomerId = customer, Lines = [Line(price)] }), default)).IsSuccess.Should().BeTrue();
            }
        }

        await using AsyncServiceScope read = await harness.OpenAsync(dossier);
        IQueryDispatcher queries = read.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        async Task<List<InvoiceSummary>> Invoices(BillingSortDto sort, SortDirectionDto direction, decimal? min = null, decimal? max = null) =>
            [.. (await queries.QueryAsync(new ListInvoicesQuery(dossier, Sort: sort, Direction: direction, MinAmount: min, MaxAmount: max), default)).Value.Items];

        (await Invoices(BillingSortDto.TotalInclTax, SortDirectionDto.Ascending)).Select(i => i.TotalInclTax).Should().Equal(60m, 360m, 1440m);
        (await Invoices(BillingSortDto.TotalExclTax, SortDirectionDto.Descending)).Select(i => i.TotalExclTax).Should().Equal(1200m, 300m, 50m);
        (await Invoices(BillingSortDto.Customer, SortDirectionDto.Ascending)).Select(i => i.CustomerName).Should()
            .Equal("Atelier Dupont", "Boulangerie Martin", "Boulangerie Martin");
        (await Invoices(BillingSortDto.Number, SortDirectionDto.Ascending)).Select(i => i.Number).Should()
            .Equal("FAC-000001", "FAC-000002", null);
        (await Invoices(BillingSortDto.Status, SortDirectionDto.Descending)).Select(i => i.Status).Should()
            .Equal(InvoiceStatusDto.Validated, InvoiceStatusDto.Validated, InvoiceStatusDto.Draft);
        (await Invoices(BillingSortDto.DueDate, SortDirectionDto.Descending)).Last().DueDate.Should().BeNull("un brouillon n'a pas encore d'échéance");
        (await Invoices(BillingSortDto.IssueDate, SortDirectionDto.Descending)).Select(i => i.Number).Should()
            .Equal(["FAC-000002", "FAC-000001", null], "à date égale, le numéro le plus récent passe en tête");

        // Bornes comprises, au centime.
        (await Invoices(BillingSortDto.TotalInclTax, SortDirectionDto.Ascending, min: 360m)).Select(i => i.TotalInclTax).Should().Equal(360m, 1440m);
        (await Invoices(BillingSortDto.TotalInclTax, SortDirectionDto.Ascending, max: 360m)).Select(i => i.TotalInclTax).Should().Equal(60m, 360m);
        (await Invoices(BillingSortDto.TotalInclTax, SortDirectionDto.Ascending, 60.01m, 1439.99m)).Select(i => i.TotalInclTax).Should().Equal(360m);

        PagedResponse<QuoteSummary> quotes = (await queries.QueryAsync(
            new ListQuotesQuery(dossier, Sort: BillingSortDto.TotalInclTax, Direction: SortDirectionDto.Descending, MinAmount: 100m), default)).Value;
        quotes.TotalCount.Should().Be(2);
        quotes.Items.Select(q => q.TotalInclTax).Should().Equal(1440m, 360m);
        (await queries.QueryAsync(new ListQuotesQuery(dossier, Sort: BillingSortDto.Customer, Direction: SortDirectionDto.Descending), default)).Value
            .Items.Select(q => q.CustomerName).Should().Equal("Boulangerie Martin", "Boulangerie Martin", "Atelier Dupont");
        (await queries.QueryAsync(new ListQuotesQuery(dossier, Sort: BillingSortDto.DueDate, Direction: SortDirectionDto.Ascending), default)).IsSuccess
            .Should().BeTrue();
    }
}
