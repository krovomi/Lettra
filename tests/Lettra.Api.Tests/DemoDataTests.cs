using System.Net.Http.Json;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Bootstrap;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Pricing;
using Lettra.DemoData;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite.Local;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.InProcess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Le jeu « Cabinet Démo » (vague 5, lane A) : tiré de façon reproductible, chargé par les cas d'usage dans la
/// composition du poste isolé, puis relu par l'application comme le ferait l'utilisateur.
/// </summary>
public sealed class DemoDataTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-demo-data-").FullName;

    [Theory]
    [InlineData(DemoBusiness.Services)]
    [InlineData(DemoBusiness.Building)]
    [InlineData(DemoBusiness.Trading)]
    public void Le_tirage_est_reproductible_et_reste_dans_le_passe(DemoBusiness business)
    {
        DemoScenario first = DemoScenarioGenerator.Generate(business, 2026, Today);
        DemoScenario second = DemoScenarioGenerator.Generate(business, 2026, Today);

        Fingerprint(second).Should().Equal(Fingerprint(first));
        Fingerprint(DemoScenarioGenerator.Generate(business, 7, Today)).Should().NotEqual(Fingerprint(first));

        first.Customers.Count.Should().BeInRange(30, 50);
        first.Suppliers.Count.Should().BeInRange(10, 15);
        first.Items.Count.Should().BeInRange(30, 70);
        first.Items.Select(i => i.Request.Reference).Should().OnlyHaveUniqueItems();
        first.Customers.Select(c => c.Request.Name).Should().OnlyHaveUniqueItems();

        DateTimeOffset todayStart = new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2));
        AllDates(first).Should().OnlyContain(d => d < todayStart && d >= first.StartsAt);
    }

    [Theory]
    [InlineData(DemoBusiness.Services)]
    [InlineData(DemoBusiness.Building)]
    [InlineData(DemoBusiness.Trading)]
    public void Les_identifiants_fictifs_passent_les_controles_du_domaine(DemoBusiness business)
    {
        DemoScenario scenario = DemoScenarioGenerator.Generate(business, 2026, Today);

        Siren.Create(scenario.Profile.Siren).IsSuccess.Should().BeTrue();
        Siret.Create(scenario.Profile.Siret).IsSuccess.Should().BeTrue();
        VatNumber.Create(scenario.Profile.VatNumber).IsSuccess.Should().BeTrue();
        Iban.Create(scenario.Profile.Iban).IsSuccess.Should().BeTrue();
        foreach (DemoCustomer customer in scenario.Customers.Where(c => c.Request.Kind == CustomerKindDto.Company))
        {
            customer.Request.Siren.Should().StartWith("9", "plage de démonstration");
            Siret.Create(customer.Request.Siret).Value.Siren.Value.Should().Be(customer.Request.Siren);
            VatNumber.Create(customer.Request.VatNumber).IsSuccess.Should().BeTrue();
        }

        scenario.Suppliers.Should().OnlyContain(s => Iban.Create(s.Request.Iban).IsSuccess && Siret.Create(s.Request.Siret).IsSuccess);
        // Aucun identifiant du parcours du README (DemoWalkthroughTests) n'est réutilisé.
        scenario.Customers.Select(c => c.Request.Siren).Should().NotContain(["900000019", "900000027"]);
    }

    [Fact]
    public void Les_donnees_sont_croisees_entre_clients_articles_et_pieces()
    {
        DemoScenario scenario = DemoScenarioGenerator.Generate(DemoBusiness.Services, 2026, Today);

        // Des articles reviennent chez beaucoup de clients, des clients reviennent souvent.
        scenario.Quotes.SelectMany(q => q.Lines.Select(l => (l.Item, q.Customer))).Distinct()
            .GroupBy(x => x.Item).Max(g => g.Count()).Should().BeGreaterThanOrEqualTo(5);
        scenario.Quotes.GroupBy(q => q.Customer).Max(g => g.Count()).Should().BeGreaterThanOrEqualTo(5);

        // Chaque histoire suit l'ordre du métier : création, validation, réponse, facture.
        foreach (DemoQuoteStory quote in scenario.Quotes)
        {
            if (quote.Outcome is not null)
            {
                quote.ValidatedAt.Should().NotBeNull().And.BeBefore(quote.Outcome.At);
            }

            if (quote.FinalInvoice is not null)
            {
                quote.Outcome!.Accepted.Should().BeTrue();
                quote.FinalInvoice.CreatedAt.Should().BeAfter(quote.Outcome.At);
            }
        }

        scenario.PriceLists.Should().NotBeEmpty();
        scenario.Quotes.SelectMany(q => q.Lines).Should().Contain(l => l.DiscountPercent > 0, "les clients d'une grille ont leur remise");
    }

    [Fact]
    public async Task Le_jeu_se_charge_par_les_cas_d_usage_dans_les_trois_dossiers_du_poste()
    {
        InMemoryKekStore keys = new();
        IConfiguration configuration = DesktopConfiguration(seed: true);
        await using ServiceProvider app = DesktopApp(configuration, keys);
        await app.InitializeLettraAsync(configuration, CancellationToken.None);
        (await app.GetRequiredService<ILocalAccounts>().CreateAsync("Camille Démo", "atelier des comptes 2026", default)).IsSuccess.Should().BeTrue();
        Guid user = app.GetRequiredService<ICurrentUser>().UserId;
        Guid opened = app.GetRequiredService<LocalDossierRegistry>().ActiveDossierId!.Value;
        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);

        DemoSeedResult result = await LettraDemoData.SeedAsync(configuration, "Demo", s => s.AddSingleton<IKekStore>(keys), user);

        result.Status.Should().Be(DemoSeedStatus.Seeded);
        result.Notes.Should().BeEmpty();
        result.Dossiers.Select(d => d.Business).Should().Equal(DemoBusiness.Services, DemoBusiness.Building, DemoBusiness.Trading);
        result.Dossiers[0].DossierId.Should().Be(opened, "le dossier ouvert reçoit le jeu principal");
        new LocalDossierRegistry(Path.Combine(_folder, "dossiers")).ActiveDossierId.Should().Be(opened, "les dossiers créés ne prennent pas la place du dossier ouvert");
        result.Dossiers.Should().OnlyContain(d => d.Quotes > 40 && d.Invoices > 30 && d.Payments > 10 && d.Transmissions > 10);

        // Relu par l'application en cours d'exécution, comme les écrans.
        IQuotesGateway quotes = app.GetRequiredService<IQuotesGateway>();
        IInvoicesGateway invoices = app.GetRequiredService<IInvoicesGateway>();
        List<QuoteSummary> allQuotes = await AllAsync((page, ct) => quotes.ListAsync(new BillingListQuery(Page: page, PageSize: 200), null, ct));
        List<InvoiceSummary> allInvoices = await AllAsync((page, ct) => invoices.ListAsync(new BillingListQuery(Page: page, PageSize: 200), null, null, ct));

        allQuotes.Should().HaveCount(result.Dossiers[0].Quotes);
        allQuotes.Select(q => q.Status).Distinct().Should().Contain([QuoteStatusDto.Draft, QuoteStatusDto.Validated, QuoteStatusDto.Accepted, QuoteStatusDto.Refused, QuoteStatusDto.Invoiced]);
        allQuotes.Should().Contain(q => q.IsExpired, "des devis restés sans réponse ont expiré");
        allInvoices.Select(i => i.Kind).Distinct().Should().Contain([InvoiceKindDto.Invoice, InvoiceKindDto.DownPayment, InvoiceKindDto.CreditNote]);
        allInvoices.Should().Contain(i => i.Status == InvoiceStatusDto.Draft).And.Contain(i => i.Status == InvoiceStatusDto.Issued);
        allInvoices.Should().Contain(i => i.Kind != InvoiceKindDto.CreditNote && i.DueDate < today && i.Status == InvoiceStatusDto.Issued, "des factures sont échues");

        (await app.GetRequiredService<ICustomersGateway>().ListAsync(1, 100, default)).Value!.TotalCount.Should().BeInRange(30, 50);
        (await app.GetRequiredService<ISuppliersGateway>().ListAsync(1, 100, default)).Value!.TotalCount.Should().BeInRange(10, 15);
        PagedResponse<CatalogItemSummary> catalog = (await app.GetRequiredService<ICatalogGateway>().ListAsync(new CatalogListQuery(IncludeArchived: true, PageSize: 200), default)).Value!;
        catalog.TotalCount.Should().BeGreaterThanOrEqualTo(30);
        catalog.Items.Count(i => i.IsArchived).Should().Be(2);
        PagedResponse<PriceListSummary> priceLists = (await app.GetRequiredService<IPriceListsGateway>().ListAsync(1, 10, default)).Value!;
        priceLists.Items.Should().NotBeEmpty().And.OnlyContain(p => p.CustomerCount > 0 && p.EntryCount > 0);

        // Pièces cohérentes : facture issue d'un devis accepté, totaux égaux à la somme des lignes, encaissements tracés.
        foreach (InvoiceSummary summary in allInvoices.Where(i => i.Status != InvoiceStatusDto.Draft).Take(40))
        {
            InvoiceResponse invoice = (await invoices.GetAsync(summary.Id, default)).Value!;
            invoice.Totals.TotalExclTax.Should().Be(invoice.Lines.Sum(l => l.TotalExclTax));
            invoice.Number.Should().StartWith("FAC-");
            if (invoice.QuoteId is Guid quoteId)
            {
                (await quotes.GetAsync(quoteId, default)).Value!.Status.Should().BeOneOf(QuoteStatusDto.Accepted, QuoteStatusDto.Invoiced);
            }
        }

        // Le prestataire a opté pour les débits : ses avoirs validés portent la mention, comme ses factures.
        InvoiceSummary credit = allInvoices.First(i => i.Kind == InvoiceKindDto.CreditNote && i.Status != InvoiceStatusDto.Draft);
        (await invoices.GetAsync(credit.Id, default)).Value!.LegalMentions.Should().Contain(m => m.Contains("débits", StringComparison.Ordinal));

        InvoiceSummary paid = allInvoices.First(i => i.Status == InvoiceStatusDto.Issued && i.Kind == InvoiceKindDto.Invoice);
        (await app.GetRequiredService<IPlatformTransmissionGateway>().GetAsync(paid.Id, default)).Value!.History
            .Should().Contain(h => h.Status == PlatformStatusDto.MadeAvailable);

        // Le FEC de l'année écoulée passe le validateur intégré : numérotation et chronologie réelles.
        IFecGateway fec = app.GetRequiredService<IFecGateway>();
        OperationResult<FecDocument> export = await fec.ExportAsync(today.AddMonths(-13), today, default);
        export.IsSuccess.Should().BeTrue(string.Join(", ", export.Errors.Select(e => e.Code)));
        FecValidationResponse report = (await fec.ValidateAsync(export.Value!.FileName, export.Value.Content, null, null, default)).Value!;
        report.IsValid.Should().BeTrue(string.Join(", ", report.Findings.Select(f => f.Code)));
        report.TotalDebit.Should().Be(report.TotalCredit);

        // La chaîne anti-fraude du dossier est intacte : chaque pièce a été scellée et journalisée dans l'ordre.
        await using (AsyncServiceScope scope = app.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(default)).IsSuccess.Should().BeTrue();
            IntegrityReportResponse integrity = (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new VerifyDossierIntegrityCommand(new DossierId(opened)), default)).Value;
            integrity.Intact.Should().BeTrue(string.Join(", ", integrity.Findings.Select(f => f.Kind)));
            integrity.DocumentsChecked.Should().BeGreaterThan(50);
        }

        // Le centre d'alertes du dossier a de quoi montrer : une alerte ouverte et une résolue.
        AlertCentreResponse alerts = (await app.GetRequiredService<IAlertsGateway>().ListAsync(cabinetWide: false, includeClosed: true, default)).Value!;
        alerts.Alerts.Select(a => a.State).Should().Contain(AlertState.Resolved).And.Contain(s => s != AlertState.Resolved);
        AlertCentreResponse cabinetAlerts = (await app.GetRequiredService<IAlertsGateway>().ListAsync(cabinetWide: true, includeClosed: true, default)).Value!;
        cabinetAlerts.Alerts.Should().HaveCountGreaterThan(alerts.Alerts.Count, "le propriétaire du poste voit aussi les alertes du cabinet");

        // Une seconde demande ne mélange rien : le dossier a déjà des pièces.
        (await LettraDemoData.SeedAsync(configuration, "Demo", s => s.AddSingleton<IKekStore>(keys), user)).Status.Should().Be(DemoSeedStatus.DossierNotEmpty);
    }

    [Fact]
    public async Task Le_jeu_se_charge_dans_le_dossier_du_serveur_de_la_demo_navigateur()
    {
        // L'API de démonstration (mode 2) : le jeu remplit le dossier configuré au nom de l'utilisateur de démonstration.
        await using LettraApiFactory api = new();
        HttpClient client = api.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        IConfiguration configuration = new ConfigurationBuilder()
            .AddConfiguration(api.Services.GetRequiredService<IConfiguration>())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lettra:Demo:Seed"] = "true",
                ["Lettra:Demo:UserId"] = LettraApiFactory.ManagerId.ToString(),
                ["Lettra:Demo:AdditionalDossiers"] = "1",
            })
            .Build();

        DemoSeedResult result = await LettraDemoData.SeedAsync(configuration, "Demo");

        result.Status.Should().Be(DemoSeedStatus.Seeded);
        result.Dossiers.Should().ContainSingle().Which.DossierId.Should().Be(LettraApiFactory.DossierId);
        result.Notes.Should().ContainSingle().Which.Should().Contain("Security.Forbidden", "un manager ne crée pas de dossier : le dossier configuré suffit");
        PagedResponse<QuoteSummary> quotes = (await client.GetFromJsonAsync<PagedResponse<QuoteSummary>>("/api/v1/quotes?pageSize=200"))!;
        quotes.TotalCount.Should().Be(result.Dossiers[0].Quotes);
        PagedResponse<InvoiceSummary> invoices = (await client.GetFromJsonAsync<PagedResponse<InvoiceSummary>>("/api/v1/invoices?pageSize=200"))!;
        invoices.TotalCount.Should().Be(result.Dossiers[0].Invoices + result.Dossiers[0].CreditNotes);
    }

    [Fact]
    public async Task Le_jeu_est_refuse_en_production_et_ignore_sans_demande()
    {
        (await LettraDemoData.SeedAsync(DesktopConfiguration(seed: true), "Production", userId: Guid.NewGuid())).Status.Should().Be(DemoSeedStatus.RefusedInProduction);
        (await LettraDemoData.SeedAsync(DesktopConfiguration(seed: false), "Demo", userId: Guid.NewGuid())).Status.Should().Be(DemoSeedStatus.NotRequested);
        (await LettraDemoData.SeedAsync(DesktopConfiguration(seed: true), "Demo")).Status.Should().Be(DemoSeedStatus.NoUser);
        LettraDemoData.IsRequested(DesktopConfiguration(seed: true)).Should().BeTrue();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private IConfiguration DesktopConfiguration(bool seed) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [LettraBootstrap.DeploymentModeKey] = "IsolatedDesktop",
        [LettraBootstrap.LocalDataDirectoryKey] = Path.Combine(_folder, "dossiers"),
        ["Lettra:LocalIdentity:StorePath"] = Path.Combine(_folder, "compte-local.json"),
        ["Lettra:LocalIdentity:Pbkdf2Iterations"] = "1000",
        ["Lettra:EInvoicing:Platform"] = "Simulated",
        ["Lettra:Demo:Seed"] = seed ? "true" : "false",
    }).Build();

    private static ServiceProvider DesktopApp(IConfiguration configuration, IKekStore keys)
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddLettra(configuration);
        collection.AddSingleton(keys);
        collection.AddSingleton<IDossierSelection>(sp => new ActiveDossier(sp.GetRequiredService<LocalDossierRegistry>()));
        collection.AddInProcessGateways();
        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<List<T>> AllAsync<T>(Func<int, CancellationToken, Task<OperationResult<PagedResponse<T>>>> page)
    {
        List<T> items = [];
        for (int i = 1; ; i++)
        {
            PagedResponse<T> current = (await page(i, default)).Value!;
            items.AddRange(current.Items);
            if (items.Count >= current.TotalCount || current.Items.Count == 0)
            {
                return items;
            }
        }
    }

    private static List<string> Fingerprint(DemoScenario scenario) =>
    [
        .. scenario.Customers.Select(c => $"{c.Request.Name}|{c.Request.Siret}|{c.CreatedAt:O}"),
        .. scenario.Items.Select(i => $"{i.Request.Reference}|{i.Request.UnitPriceExclTax}"),
        .. scenario.Quotes.Select(q => $"{q.Customer}|{q.CreatedAt:O}|{q.Lines.Count}|{q.Outcome?.Accepted}|{q.FinalInvoice?.Payments.Count}"),
        .. scenario.Invoices.Select(i => $"{i.Customer}|{i.Invoice.CreatedAt:O}"),
    ];

    private static IEnumerable<DateTimeOffset> AllDates(DemoScenario scenario) =>
        scenario.Customers.Select(c => c.CreatedAt)
            .Concat(scenario.Items.Select(i => i.CreatedAt))
            .Concat(scenario.Suppliers.Select(s => s.CreatedAt))
            .Concat(scenario.PriceLists.Select(p => p.CreatedAt))
            .Concat(scenario.Quotes.SelectMany(q => new[] { q.CreatedAt, q.ValidatedAt, q.Outcome?.At }.OfType<DateTimeOffset>()
                .Concat(Dates(q.DownPayment?.Invoice)).Concat(Dates(q.FinalInvoice))))
            .Concat(scenario.Invoices.SelectMany(i => Dates(i.Invoice)))
            .Append(scenario.ArchivedAt);

    private static IEnumerable<DateTimeOffset> Dates(DemoSettledInvoice? invoice) => invoice is null
        ? []
        : new[] { invoice.CreatedAt, invoice.ValidatedAt, invoice.TransmittedAt, invoice.Credit?.At }.OfType<DateTimeOffset>().Concat(invoice.Payments.Select(p => p.At));

    /// <summary>Comme l'application de bureau : le dossier ouvert est celui du registre local.</summary>
    private sealed class ActiveDossier(LocalDossierRegistry registry) : IDossierSelection
    {
        public Guid? CurrentDossierId => registry.ActiveDossierId;
    }
}
