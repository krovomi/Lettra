using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Catalog.Kpi;
using Lettra.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Indicateurs de pilotage pour Grafana sur les trois bases : pièces lues dans la base du dossier, taille de la base,
/// postes, puis collecte par le catalogue (noms des dossiers, comptes, compteurs de la vue cabinet).
/// </summary>
internal static class KpiScenarios
{
    private static readonly Customer.Draft Durand = new(
        CustomerKind.Company, "Atelier Durand", "732829320", "73282932000074", "FR44732829320", "compta@durand.example",
        "3 rue des Lilas", null, "69001", "Lyon", "FR");

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Martin Conseil", "552081317", "55208131766522", "FR03552081317", "compta@martin.example",
        "8 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Fictif", "SAS", 10_000m, "552100554", "55210055400013", "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", VatFranchise: false, VatOnDebits: false, PaymentDays: 30,
        PaymentTerms: "Virement à 30 jours", LatePenaltyRate: 10m, EarlyPaymentDiscount: "Pas d'escompte pour paiement anticipé",
        Iban: "FR7630006000011234567890189", Bic: "AGRIFRPP", VatDeclarationRegime: VatDeclarationRegime.RealNormal);

    /// <summary>Sources lues dans la base d'un dossier : pièces validées, taille de la base, postes.</summary>
    public static async Task DossierSourcesAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            Ok(await commands.SendAsync(new SaveCompanyProfileCommand(dossier, Cabinet, null), default));
            Guid durand = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Durand), default)).Value;
            Guid martin = Ok(await commands.SendAsync(new CreateCustomerCommand(dossier, Martin), default)).Value;

            // 300 € HT chez Durand, 500 € HT chez Martin ; un brouillon n'est jamais lu.
            foreach ((Guid customer, decimal price, bool validate) in new[] { (durand, 100m, true), (martin, 500m, true), (martin, 900m, false) })
            {
                SaveInvoiceRequest request = new() { CustomerId = customer, Lines = [Line(customer == durand ? 3 : 1, price)] };
                InvoiceId invoice = Ok(await commands.SendAsync(new CreateInvoiceCommand(dossier, request), default));
                if (validate)
                {
                    Ok(await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoice, null), default));
                }
            }

            QuoteId quote = Ok(await commands.SendAsync(new CreateQuoteCommand(dossier, new SaveQuoteRequest { CustomerId = durand, Lines = [Line(2, 50m)] }), default));
            Ok(await commands.SendAsync(new ValidateQuoteCommand(dossier, quote, null), default));

            ISyncDeviceRegistry devices = scope.ServiceProvider.GetRequiredService<ISyncDeviceRegistry>();
            (await devices.TouchAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddDays(-10), default)).IsSuccess.Should().BeTrue();
            (await devices.TouchAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, default)).IsSuccess.Should().BeTrue();
            Guid revoked = Guid.CreateVersion7();
            (await devices.TouchAsync(revoked, Guid.CreateVersion7(), DateTimeOffset.UtcNow, default)).IsSuccess.Should().BeTrue();
            (await devices.RevokeAsync(revoked, Guid.CreateVersion7(), DateTimeOffset.UtcNow, default)).Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            BillingKpiFacts facts = await scope.ServiceProvider.GetRequiredService<IBillingKpiReader>().ReadAsync(default);
            facts.Invoices.Should().HaveCount(2).And.OnlyContain(i => i.Kind == InvoiceKind.Invoice && i.IssueDate == today && i.DueDate > today);
            facts.Invoices.Select(i => i.TotalExclTax).Should().BeEquivalentTo([300m, 500m]);
            facts.Invoices.Select(i => i.TotalInclTax).Should().BeEquivalentTo([360m, 600m]);
            facts.Quotes.Should().ContainSingle().Which.Status.Should().Be(QuoteStatus.Validated);

            List<KpiSample> samples = [];
            foreach (IDossierKpiSource source in scope.ServiceProvider.GetServices<IDossierKpiSource>())
            {
                samples.AddRange(await source.CollectAsync(today, default));
            }

            Value(samples, KpiMetrics.ActiveCustomers).Should().Be(2);
            Value(samples, KpiMetrics.TopCustomerShare).Should().Be(0.625, "Martin fait 500 € HT sur 800 €");
            Value(samples, KpiMetrics.DaysSalesOutstanding).Should().Be(90, "rien n'est encaissé sur 90 jours de ventes");
            Value(samples, KpiMetrics.OverdueReceivables).Should().Be(0);
            Value(samples, KpiMetrics.QuotesPending).Should().Be(1);
            samples.Single(s => s.Metric == KpiMetrics.InvoicesIssued && s.Label!.Value == today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).Value.Should().Be(2);
            Value(samples, KpiMetrics.DatabaseSize).Should().BePositive();
            samples.Single(s => s.Metric == KpiMetrics.Devices && s.Label!.Value == "active").Value.Should().Be(2);
            samples.Single(s => s.Metric == KpiMetrics.Devices && s.Label!.Value == "revoked").Value.Should().Be(1);
            Value(samples, KpiMetrics.SilentDevices).Should().Be(1, "un poste se tait depuis 10 jours");
            Value(samples, KpiMetrics.OldestDeviceSyncAge).Should().BeGreaterThan(TimeSpan.FromDays(9).TotalSeconds);
            samples.Should().OnlyContain(s => s.Dossier == null, "le collecteur estampille le dossier");
        }
    }

    /// <summary>Collecte par le catalogue : dossiers actifs nommés, comptes et dossiers par état, vue cabinet.</summary>
    public static async Task CatalogCollectionAsync(CatalogHarness harness)
    {
        IServiceProvider services = harness.Services;
        await using (CatalogDbContext db = await services.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        await services.InitializeCatalogAsync(migrate: true, default);
        DossierId first = (await services.GetRequiredService<IDossierProvisioner>().CreateAsync(CatalogHarness.Organisation, "Boulangerie \"Le Fournil\"", default)).Value;
        DossierId second = (await services.GetRequiredService<IDossierProvisioner>().CreateAsync(CatalogHarness.Organisation, "Atelier Martin", default)).Value;
        (await services.GetRequiredService<IDossierIndicatorRefresher>().RefreshAsync([first], default)).Refreshed.Should().Be(1);

        KpiSnapshotCollector collector = services.GetRequiredService<KpiSnapshotCollector>();
        KpiSnapshot snapshot = await collector.GetAsync(default);

        snapshot.Dossiers.Select(d => d.Name).Should().Equal("Atelier Martin", "Boulangerie \"Le Fournil\"");
        snapshot.Dossiers.Should().OnlyContain(d => d.Cabinet == "Cabinet de test");
        snapshot.Samples.Where(s => s.Metric == KpiMetrics.DossierInfo).Select(s => s.Dossier).Should().BeEquivalentTo(new DossierId?[] { first, second });
        snapshot.Samples.Where(s => s.Metric == KpiMetrics.DatabaseSize).Should().HaveCount(2).And.OnlyContain(s => s.Value > 0);
        snapshot.Samples.Single(s => s.Metric == KpiMetrics.Dossiers && s.Label!.Value == "active").Value.Should().Be(2);
        snapshot.Samples.Single(s => s.Metric == KpiMetrics.Accounts && s.Label!.Value == "active").Value.Should().BeGreaterThanOrEqualTo(1);
        snapshot.Samples.Where(s => s.Metric == KpiMetrics.CabinetIndicatorCount).Should().NotBeEmpty().And.OnlyContain(s => s.Dossier == first);
        snapshot.Samples.Single(s => s.Metric == KpiMetrics.CabinetIndicatorAge).Dossier.Should().Be(first);
        snapshot.Samples.Single(s => s.Metric == KpiMetrics.CollectionFailures).Value.Should().Be(0);

        // Lue de nouveau dans l'intervalle, la collecte est resservie telle quelle (aucune base rouverte).
        (await collector.GetAsync(default)).Should().BeSameAs(snapshot);

        // Une base de dossier injoignable : ses sources sont comptées en échec, le dossier reste listé sans valeur, et
        // les autres dossiers sont collectés.
        await harness.UpdateDossierAsync(second, d => d.ConnectionSecretRef = "cluster:inconnu/base");
        KpiSnapshot degraded = await collector.CollectAsync(default);
        degraded.Samples.Single(s => s.Metric == KpiMetrics.CollectionFailures).Value.Should().BePositive();
        degraded.Samples.Where(s => s.Metric == KpiMetrics.DatabaseSize).Should().ContainSingle().Which.Dossier.Should().Be(first);
    }

    private static DocumentLineRequest Line(decimal quantity, decimal price) =>
        new() { Designation = "Conseil", Quantity = quantity, UnitPriceExclTax = price, VatRateCode = "FR-20", Kind = CatalogItemKindDto.Service, UnitCode = "HUR" };

    private static double Value(IEnumerable<KpiSample> samples, string metric) => samples.Single(s => s.Metric == metric).Value;

    private static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Value;
    }

    private static void Ok(Result result) => result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
}
