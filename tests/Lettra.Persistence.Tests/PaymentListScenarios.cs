using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Liste des encaissements cherchée, triée et paginée en base : même résultat sur les trois providers, quelle que soit
/// la culture du serveur. Données fictives.
/// </summary>
internal static class PaymentListScenarios
{
    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: true, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    private static Customer.Draft Customer(string name) => new(
        CustomerKind.Individual, name, null, null, null, null, "1 rue de la Paix", null, "75002", "Paris", "FR");

    public static async Task SearchSortAndPageInTheDatabaseAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);
        InvoiceId first;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Ok(await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default));
            Guid eclair = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Customer("Boulangerie Éclair")), default)).Value;
            Guid martin = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Customer("Garage Martin")), default)).Value;
            first = await ValidatedAsync(commands, dossier, eclair);
            InvoiceId second = await ValidatedAsync(commands, dossier, eclair);
            InvoiceId third = await ValidatedAsync(commands, dossier, martin);
            await PayAsync(commands, dossier, eclair, first, 60m, today.AddDays(-2), "vir-sepa-0042");
            await PayAsync(commands, dossier, eclair, second, 120m, today.AddDays(-1), null);
            await PayAsync(commands, dossier, martin, third, 100m, today, null);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            string invoiceNumber = Ok(await queries.QueryAsync(new GetInvoiceQuery(dossier, first), default)).Number!;
            (await ListAsync(queries, new ListPaymentsQuery(dossier, Search: "eclair"))).Select(p => p.CustomerName)
                .Should().Equal(["Boulangerie Éclair", "Boulangerie Éclair"], "le nom se cherche sans accents ni casse");
            (await ListAsync(queries, new ListPaymentsQuery(dossier, Search: "SEPA-0042"))).Select(p => p.Amount).Should().Equal([60m]);
            (await ListAsync(queries, new ListPaymentsQuery(dossier, Search: invoiceNumber.ToLowerInvariant()))).Select(p => p.Amount)
                .Should().Equal([60m], "le numéro de la facture imputée est cherché aussi");
            (await ListAsync(queries, new ListPaymentsQuery(dossier, MinAmount: 70m, MaxAmount: 110m))).Select(p => p.Amount).Should().Equal([100m]);
            (await ListAsync(queries, new ListPaymentsQuery(dossier))).Select(p => p.Date)
                .Should().Equal([today, today.AddDays(-1), today.AddDays(-2)], "du plus récent au plus ancien par défaut");
            (await ListAsync(queries, new ListPaymentsQuery(dossier, Sort: PaymentSortDto.Amount, Direction: SortDirectionDto.Ascending)))
                .Select(p => p.Amount).Should().Equal([60m, 100m, 120m]);
            (await ListAsync(queries, new ListPaymentsQuery(dossier, Sort: PaymentSortDto.Customer, Direction: SortDirectionDto.Descending)))
                .Select(p => p.CustomerName).Should().Equal(["Garage Martin", "Boulangerie Éclair", "Boulangerie Éclair"]);

            Result<PagedResponse<PaymentSummary>> page = await queries.QueryAsync(
                new ListPaymentsQuery(dossier, Page: 2, PageSize: 2, Sort: PaymentSortDto.Amount, Direction: SortDirectionDto.Ascending), default);
            page.Value.TotalCount.Should().Be(3);
            page.Value.Items.Select(p => p.Amount).Should().Equal([120m]);

            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            (await db.Set<Payment>().Select(p => EF.Property<string>(p, SearchKey.PropertyName)).ToListAsync())
                .Should().Contain("BOULANGERIE ECLAIR", "la clé de recherche est écrite avec l'encaissement");
        }
    }

    private static async Task<IReadOnlyList<PaymentSummary>> ListAsync(IQueryDispatcher queries, ListPaymentsQuery query) =>
        Ok(await queries.QueryAsync(query, default)).Items;

    private static async Task<InvoiceId> ValidatedAsync(ICommandDispatcher commands, DossierId dossier, Guid customer)
    {
        DocumentLineRequest line = new() { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = 100m, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };
        InvoiceId created = Ok(await commands.SendAsync(new CreateInvoiceCommand(dossier, new SaveInvoiceRequest { CustomerId = customer, Lines = [line] }), default));
        Ok(await commands.SendAsync(new ValidateInvoiceCommand(dossier, created, null), default));
        return created;
    }

    private static async Task PayAsync(ICommandDispatcher commands, DossierId dossier, Guid customer, InvoiceId invoice, decimal amount, DateOnly date, string? reference) =>
        Ok(await commands.SendAsync(new RecordPaymentCommand(dossier, new RecordPaymentRequest
        {
            CustomerId = customer,
            Date = date,
            Amount = amount,
            Reference = reference,
            Allocations = [new PaymentAllocationRequest(invoice.Value, amount)],
        }), default));

    private static void Ok(Result result) => result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));

    private static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }
}
