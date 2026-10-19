using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Factures récurrentes sur les trois bases : modèles et lignes relus, filtres et tris traduits en SQL, passe de fond
/// qui crée les factures dues (brouillon ou validée avec numéro et période), puis ne recrée rien à la passe suivante.
/// </summary>
internal static class RecurringScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: null, Bic: null, VatDeclarationRegime: null);

    public static async Task RecurringInvoicesAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly start = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        RecurringInvoiceId drafted, validated;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
            drafted = (await commands.SendAsync(new CreateRecurringInvoiceCommand(dossier, Template(durand, "Brouillon mensuel", start, false, 80m)), default)).Value;
            validated = (await commands.SendAsync(new CreateRecurringInvoiceCommand(dossier, Template(durand, "Validé mensuel", start, true, 120m)), default)).Value;
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            RecurringInvoiceResponse read = (await queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, validated), default)).Value;
            read.Lines.Should().ContainSingle().Which.UnitPriceExclTax.Should().Be(120m);
            read.Revision.Should().Be(PriceRevisionKindDto.FixedPercent);
            read.RevisionPercent.Should().Be(2.5m);
            read.IsDue.Should().BeTrue();

            foreach (RecurringInvoiceSortDto sort in Enum.GetValues<RecurringInvoiceSortDto>())
            {
                PagedResponse<RecurringInvoiceSummary> sorted = (await queries.QueryAsync(
                    new ListRecurringInvoicesQuery(dossier, "mensuel", null, true, true, sort, true, 1, 50), default)).Value;
                sorted.TotalCount.Should().Be(2, sort.ToString());
            }

            PagedResponse<RecurringInvoiceSummary> byAmount = (await queries.QueryAsync(
                new ListRecurringInvoicesQuery(dossier, "durand", null, null, false, RecurringInvoiceSortDto.Amount, true, 1, 50), default)).Value;
            byAmount.Items.Select(i => i.TotalExclTax).Should().Equal(120m, 80m);
        }

        TenantDescriptor[] dossiers = [harness.Descriptor(dossier)];
        RecurringInvoiceJob job = new(
            harness.Services,
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            new RecurringDossierSource((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)),
            Options.Create(new RecurringInvoiceJobOptions()),
            NullLogger<RecurringInvoiceJob>.Instance);

        (int created, int validatedCount, int failed) = await job.RunPassAsync(default);
        created.Should().Be(4, "deux périodes dues pour chacun des deux modèles");
        validatedCount.Should().Be(2);
        failed.Should().Be(0);
        (await job.RunPassAsync(default)).Created.Should().Be(0, "une période déjà facturée ne l'est pas deux fois");

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            RecurringInvoiceResponse template = (await queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, validated), default)).Value;
            template.Occurrences.Should().HaveCount(2).And.OnlyContain(o => o.InvoiceStatus == InvoiceStatusDto.Validated && o.InvoiceNumber != null);
            InvoiceResponse invoice = (await queries.QueryAsync(new GetInvoiceQuery(dossier, new InvoiceId(template.Occurrences[^1].InvoiceId)), default)).Value;
            invoice.BillingPeriodStart.Should().Be(start);
            invoice.BillingPeriodEnd.Should().Be(start.AddMonths(1).AddDays(-1));
            (await queries.QueryAsync(new GetRecurringInvoiceQuery(dossier, drafted), default)).Value
                .Occurrences.Should().HaveCount(2).And.OnlyContain(o => o.InvoiceStatus == InvoiceStatusDto.Draft);
        }
    }

    private static SaveRecurringInvoiceRequest Template(Guid customer, string name, DateOnly start, bool validate, decimal price) => new()
    {
        Name = name,
        CustomerId = customer,
        Lines = [new DocumentLineRequest { Designation = "Forfait", Quantity = 1, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "C62" }],
        StartDate = start,
        EndDate = start.AddMonths(2).AddDays(-1),
        ValidateAutomatically = validate,
        Revision = PriceRevisionKindDto.FixedPercent,
        RevisionPercent = 2.5m,
    };
}
