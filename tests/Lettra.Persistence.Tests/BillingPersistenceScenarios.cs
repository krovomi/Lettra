using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Integrity;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Devis et facturation : mêmes scénarios sur SQLite, PostgreSQL et SQL Server.</summary>
internal static class BillingPersistenceScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", "73282932000074", "FR44732829320", "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP");

    private static DocumentLineRequest Line(string designation, decimal quantity, decimal price, string vat = "FR-20", CatalogItemKindDto kind = CatalogItemKindDto.Service) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = vat, Kind = kind, UnitCode = kind == CatalogItemKindDto.Service ? "HUR" : "C62" };

    /// <summary>
    /// Le parcours complet : devis modifié puis validé (DEV), accepté, acompte de 30 % (FAC-1), facture finale qui le
    /// déduit (FAC-2), avoir partiel (FAC-3). Lignes, TVA, copies des tiers, empreintes et écritures sont relues en base.
    /// </summary>
    public static async Task LifecycleAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Guid customer;
        QuoteId quote;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            SaveQuoteRequest request = new()
            {
                CustomerId = customer,
                Subject = "Mission d'accompagnement",
                Lines = [Line("Audit", 3, 100m), Line("Livre « Gestion »", 2, 19.99m, "FR-5.5", CatalogItemKindDto.Good)],
            };
            quote = (await commands.SendAsync(new CreateQuoteCommand(dossier, request), default)).Value;

            // Lignes remplacées au même rang : la pièce garde un seul jeu de lignes.
            request.Lines = [Line("Audit", 4, 100m), Line("Livre « Gestion »", 2, 19.99m, "FR-5.5", CatalogItemKindDto.Good), Line("Frais", 1, 50m)];
            (await commands.SendAsync(new UpdateQuoteCommand(dossier, quote, request, 1), default)).IsSuccess.Should().BeTrue();
        }

        InvoiceId downPayment;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            QuoteResponse draft = (await queries.QueryAsync(new GetQuoteQuery(dossier, quote), default)).Value;
            draft.Lines.Should().HaveCount(3);
            draft.Totals.TotalExclTax.Should().Be(489.98m);
            draft.Totals.TotalVat.Should().Be(92.20m);

            (await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, draft.Version), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new SetQuoteOutcomeCommand(dossier, quote, true, null), default)).IsSuccess.Should().BeTrue();
            downPayment = (await commands.SendAsync(new CreateDownPaymentCommand(dossier, quote, new CreateDownPaymentRequest { Percent = 30m }), default)).Value;
            Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, downPayment, null), default);
            validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        }

        InvoiceId final;
        InvoiceId credit;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            QuoteResponse accepted = (await queries.QueryAsync(new GetQuoteQuery(dossier, quote), default)).Value;
            accepted.Number.Should().Be("DEV-000001");
            accepted.DownPaymentsInclTax.Should().Be(174.65m);

            final = (await commands.SendAsync(new CreateFinalInvoiceCommand(dossier, quote), default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, final, null), default)).IsSuccess.Should().BeTrue();
            credit = (await commands.SendAsync(
                new CreateCreditNoteCommand(dossier, final, new CreateCreditNoteRequest { Reason = "Une heure non réalisée", Lines = [new CreditNoteLineRequest(1, 1m)] }),
                default)).Value;
            (await commands.SendAsync(new ValidateInvoiceCommand(dossier, credit, null), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new IssueInvoiceCommand(dossier, final, null), default)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            InvoiceResponse dp = (await queries.QueryAsync(new GetInvoiceQuery(dossier, downPayment), default)).Value;
            InvoiceResponse fi = (await queries.QueryAsync(new GetInvoiceQuery(dossier, final), default)).Value;
            InvoiceResponse cn = (await queries.QueryAsync(new GetInvoiceQuery(dossier, credit), default)).Value;

            dp.Number.Should().Be("FAC-000001");
            dp.Kind.Should().Be(InvoiceKindDto.DownPayment);
            dp.Totals.TotalInclTax.Should().Be(174.65m);
            fi.Number.Should().Be("FAC-000002");
            fi.Status.Should().Be(InvoiceStatusDto.Issued);
            fi.Totals.TotalInclTax.Should().Be(582.18m - 174.65m);
            fi.Seller!.Siren.Should().Be("552100554");
            fi.Customer.Siren.Should().Be("732829320");
            fi.CreditedInclTax.Should().Be(cn.Totals.TotalInclTax);
            cn.Number.Should().Be("FAC-000003");
            cn.CreditedInvoiceNumber.Should().Be("FAC-000002");
            cn.Totals.TotalInclTax.Should().Be(120m);

            foreach (InvoiceResponse invoice in new[] { dp, fi, cn })
            {
                invoice.ContentHash.Should().Be(CanonicalDocument.ComputeHash(invoice));
            }

            IReadOnlyDictionary<Guid, string> recomputed = await scope.ServiceProvider.GetRequiredService<IJournaledDocumentReader>()
                .ComputeContentHashesAsync([dp.Id, fi.Id, cn.Id, quote.Value], default);
            recomputed[fi.Id].Should().Be(fi.ContentHash);
            recomputed.Should().ContainKey(quote.Value);

            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            List<SalesJournalRecord> entries = await db.Set<SalesJournalRecord>().Include(e => e.Lines).ToListAsync();
            entries.Should().HaveCount(3);
            entries.Should().OnlyContain(e => e.Lines.Sum(l => l.Debit) == e.Lines.Sum(l => l.Credit));
            entries.Single(e => e.PieceNumber == "FAC-000001").Lines.Should().Contain(l => l.Account == SalesAccounts.DownPaymentsReceived);

            PagedResponse<InvoiceSummary> list = (await queries.QueryAsync(new ListInvoicesQuery(dossier, Search: "martin"), default)).Value;
            list.TotalCount.Should().Be(3);
            list.Items.Select(i => i.Number).Should().Equal("FAC-000003", "FAC-000002", "FAC-000001");

            // La chaîne anti-fraude relit les pièces en base : chaque empreinte journalisée est celle de la pièce.
            IntegrityReport integrity = (await scope.ServiceProvider.GetRequiredService<DossierIntegrityService>().VerifyAsync(default)).Value;
            integrity.Findings.Should().BeEmpty();
            integrity.DocumentsChecked.Should().Be(4);
        }
    }

    /// <summary>
    /// Validations concurrentes de factures différentes : chaque pièce a un numéro, sans trou ni doublon ; la même
    /// facture validée deux fois en même temps ne l'est qu'une fois.
    /// </summary>
    public static async Task ConcurrentValidationsAsync(TenantHarness harness, DatabaseProvider provider, string connectionString, int parallel = 16)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        List<InvoiceId> drafts = [];
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Guid customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
            (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
            for (int i = 0; i < parallel; i++)
            {
                SaveInvoiceRequest request = new() { CustomerId = customer, Lines = [Line($"Prestation {i}", 1, 10m + i)] };
                drafts.Add((await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default)).Value);
            }
        }

        // Chaque facture est validée deux fois en parallèle : une seule validation doit l'emporter.
        Result[] results = await Task.WhenAll(drafts.Concat(drafts).Select(async id =>
        {
            await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
            return await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new ValidateInvoiceCommand(dossier, id, null), default);
        }));

        results.Count(r => r.IsSuccess).Should().Be(parallel);
        results.Where(r => r.IsFailure).Should().OnlyContain(r => r.Errors[0].Code == ErrorCodes.Billing.NotDraft);

        await using AsyncServiceScope check = await harness.OpenAsync(dossier);
        LettraDbContext db = await check.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
        List<string?> numbers = await db.Set<Invoice>().Select(i => i.Number).ToListAsync();
        numbers.Should().OnlyHaveUniqueItems();
        numbers.Order(StringComparer.Ordinal).Should().Equal(Enumerable.Range(1, parallel).Select(i => $"FAC-{i:D6}"));
        (await db.Set<SalesJournalRecord>().CountAsync()).Should().Be(parallel);
    }

    /// <summary>Une pièce modifiée par quelqu'un d'autre depuis sa lecture n'est pas écrasée.</summary>
    public static async Task StaleVersionAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        Guid customer = (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value.Value;
        SaveInvoiceRequest request = new() { CustomerId = customer, Lines = [Line("Conseil", 1, 100m)] };
        InvoiceId id = (await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default)).Value;
        (await commands.SendAsync(new UpdateInvoiceCommand(dossier, id, request, 1), default)).IsSuccess.Should().BeTrue();

        Result stale = await commands.SendAsync(new UpdateInvoiceCommand(dossier, id, request, 1), default);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        (await commands.SendAsync(new ValidateInvoiceCommand(dossier, id, 2), default)).Errors[0].Code
            .Should().Be(ErrorCodes.Billing.CompanyProfileMissing);
    }
}
