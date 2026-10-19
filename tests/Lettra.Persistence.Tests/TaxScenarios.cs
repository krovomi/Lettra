using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Tax;
using Lettra.Exports.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Déclarations (module Exports) sur les trois bases : brouillon de CA3 et DES lus depuis les pièces enregistrées,
/// réglages fiscaux et dépôts marqués qui survivent à une nouvelle session, passe d'alertes du calendrier.
/// </summary>
internal static class TaxScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Berlin = new(
        CustomerKind.Company, "Berlin Beratung GmbH", null, null, "DE123456789", "buchhaltung@berlin.example",
        "Unter den Linden 1", null, "10117", "Berlin", "DE");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: true, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    public static async Task DeclarationsAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(harness, dossier);

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            Result<VatReturnResponse> vat = await queries.QueryAsync(new GetVatReturnQuery(dossier, today), default);
            vat.IsSuccess.Should().BeTrue(string.Join(", ", vat.Errors.Select(e => e.Code)));
            vat.Value.Form.Should().Be(VatReturnFormDto.Ca3);
            vat.Value.Boxes.Single(b => b.Key == "Rate20").VatAmount.Should().Be(60m);
            vat.Value.Boxes.Single(b => b.Key == "E2").BaseExclTax.Should().Be(500m, "la prestation à Berlin est autoliquidée");
            vat.Value.Checks.Single(c => c.Code == VatReturnCalculator.CheckJournal).Severity.Should().Be(TaxCheckSeverityDto.Ok);
            vat.Value.Checks.Single(c => c.Code == VatReturnCalculator.CheckNotTransmitted).Count.Should().Be(1, "la facture à Durand n'est pas transmise");

            Result<DesResponse> des = await queries.QueryAsync(new GetDesQuery(dossier, today.Year, today.Month), default);
            des.Value.Lines.Should().ContainSingle().Which.Should().Match<DesLineResponse>(l => l.CustomerVatNumber == "DE123456789" && l.Amount == 500m);

            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Result<FiscalSettingsResponse> saved = await commands.SendAsync(new UpdateFiscalSettingsCommand(dossier,
                new UpdateFiscalSettingsRequest(12, 31, VatPeriodicityDto.Monthly, 19, true, true, 0)), default);
            saved.IsSuccess.Should().BeTrue(string.Join(", ", saved.Errors.Select(e => e.Code)));

            DateOnly period = new(today.Year, today.Month, 1);
            Result<FiscalDeadlineResponse> marked = await commands.SendAsync(new MarkFiscalFilingCommand(dossier,
                new MarkFiscalFilingRequest(FiscalObligationDto.VatReturnCa3, period, true)), default);
            marked.IsSuccess.Should().BeTrue(string.Join(", ", marked.Errors.Select(e => e.Code)));
        }

        // Une nouvelle session relit les réglages et le dépôt enregistrés.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            FiscalSettingsResponse settings = (await queries.QueryAsync(new GetFiscalSettingsQuery(dossier), default)).Value;
            settings.Should().Match<FiscalSettingsResponse>(s => s.Version == 1 && s.VatDeadlineDay == 19 && s.TrackedFrom != null);
            VatReturnResponse vat = (await queries.QueryAsync(new GetVatReturnQuery(dossier, today), default)).Value;
            vat.FiledAt.Should().NotBeNull();
            vat.DueDate.Day.Should().Be(19);

            // La vue cabinet lit « TVA à déclarer » dans la base du dossier, comme le calendrier.
            IReadOnlyList<FiscalDeadlineResponse> upcoming = (await queries.QueryAsync(new GetUpcomingFiscalDeadlinesQuery(dossier), default)).Value;
            DossierIndicator expected = VatToDeclareIndicatorSource.Indicator(upcoming);
            VatToDeclareIndicatorSource source = scope.ServiceProvider.GetServices<IDossierIndicatorSource>().OfType<VatToDeclareIndicatorSource>().Single();
            (await source.ComputeAsync(today, default)).Should().ContainSingle().Which.Should().Be(expected);
        }

        TenantDescriptor[] dossiers = [harness.Descriptor(dossier)];
        FiscalDeadlineAlertJob job = new(
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            harness.Services,
            new FiscalDossierSource((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>(dossiers)),
            TimeProvider.System,
            Options.Create(new FiscalDeadlineAlertOptions()),
            NullLogger<FiscalDeadlineAlertJob>.Instance);

        (int checkedCount, _, _, int failed) = await job.RunPassAsync(default);
        checkedCount.Should().Be(1);
        failed.Should().Be(0);
        harness.Alerts.Signals.Where(s => s.Dossier == dossier).Select(s => s.Rule).Should()
            .BeEquivalentTo([DossierAlertRule.FiscalDeadlineSoon, DossierAlertRule.FiscalDeadlineOverdue]);
    }

    private static DocumentLineRequest Service(string designation, decimal quantity, decimal price) =>
        new() { Designation = designation, Quantity = quantity, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    private static async Task SeedAsync(TenantHarness harness, DossierId dossier)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        (await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();
        Guid durand = (await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value.Value;
        Guid berlin = (await commands.SendAsync(new CreateCustomerCommand(dossier, Berlin), default)).Value.Value;

        foreach ((Guid customer, DocumentLineRequest line, VatRegimeDto regime) in new[]
        {
            (durand, Service("Conseil", 3, 100m), VatRegimeDto.Standard),
            (berlin, Service("Audit", 5, 100m), VatRegimeDto.ReverseChargeIntraEu),
        })
        {
            SaveInvoiceRequest request = new() { CustomerId = customer, Lines = [line], VatRegime = regime };
            Result<InvoiceId> created = await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default);
            created.IsSuccess.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
            Result validated = await commands.SendAsync(new ValidateInvoiceCommand(dossier, created.Value, null), default);
            validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        }
    }
}
