using System.Reflection;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Pricing;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

public sealed class BillingTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", "732829320", null, "FR44732829320", null, "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly CompanyProfile.Draft Cabinet = new(
        "Conseil Lettra", "SAS", 10_000m, "552100554", null, "RCS Paris 552 100 554", "FR96552100554",
        "10 avenue de l'Opéra", null, "75001", "Paris", "FR", false, false, 30, null, 10m, "Pas d'escompte", null, null);

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakePermissions _permissions = new();
    private readonly InMemoryCustomers _customers = new();
    private readonly InMemoryPartiesAndCatalog _catalog = new();
    private readonly InMemoryBilling _billing = new();
    private ServiceProvider _root = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddSingleton<ICurrentUser>(new FakeUser());
        services.AddSingleton<IPermissionService>(_permissions);
        services.AddSingleton<ICustomerRepository>(_customers);
        services.AddSingleton<ICustomerReadStore>(_customers);
        services.AddSingleton<IUnitOfWork>(_customers);
        // Le faux de facturation enregistre son propre journal après celui-ci : c'est lui que les tests interrogent.
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
        services.AddPartiesAndCatalogFakes(_catalog, new FakeDirectories(), _billing);
        services.AddSingleton<IDossierProvisioner>(new RecordingDossierProvisioner());
        services.AddSingleton<IAuthorizationDirectory>(new EmptyAuthorizationDirectory());
        services.AddSingleton<IDossierDirectory>(new EmptyDossierDirectory());
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _root.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    private async Task<InvoiceId> DraftInvoiceAsync()
    {
        Guid customer = (await Commands.SendAsync(new CreateCustomerCommand(Dossier, Martin), default)).Value.Value;
        SaveInvoiceRequest request = new()
        {
            CustomerId = customer,
            Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 2, UnitPriceExclTax = 150m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
        };
        return (await Commands.SendAsync(new CreateInvoiceCommand(Dossier, request), default)).Value;
    }

    [Fact]
    public async Task Valider_une_facture_numerote_scelle_journalise_et_comptabilise()
    {
        InvoiceId id = await DraftInvoiceAsync();
        (await Commands.SendAsync(new SaveCompanyProfileCommand(Dossier, Cabinet, null), default)).IsSuccess.Should().BeTrue();

        Result validated = await Commands.SendAsync(new ValidateInvoiceCommand(Dossier, id, 1), default);

        validated.IsSuccess.Should().BeTrue(string.Join(", ", validated.Errors.Select(e => e.Code)));
        InvoiceResponse invoice = (await Queries.QueryAsync(new GetInvoiceQuery(Dossier, id), default)).Value;
        invoice.Number.Should().Be("FAC000001");
        invoice.ContentHash.Should().Be(CanonicalDocument.ComputeHash(invoice)).And.MatchRegex("^[0-9a-f]{64}$");
        DocumentLifecycleEvent journaled = _billing.Journal.Should().ContainSingle().Subject;
        journaled.Should().BeEquivalentTo(new
        {
            Kind = JournalEntryKind.InvoiceValidated,
            DocumentKind = JournaledDocumentKind.Invoice,
            DocumentId = id.Value,
            Number = "FAC000001",
            TotalInclTax = 360m,
            Currency = "EUR",
            invoice.ContentHash,
        });
        SalesJournalEntry entry = _billing.SalesEntries.Should().ContainSingle().Subject;
        entry.Lines.Sum(l => l.Debit).Should().Be(360m).And.Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Supprimer_un_client_le_retire_des_listes_et_garde_ses_factures()
    {
        InvoiceId id = await DraftInvoiceAsync();
        Guid customer = (await Queries.QueryAsync(new GetInvoiceQuery(Dossier, id), default)).Value.CustomerId;

        Result deleted = await Commands.SendAsync(new DeleteCustomerCommand(Dossier, new CustomerId(customer), null), default);
        Result again = await Commands.SendAsync(new DeleteCustomerCommand(Dossier, new CustomerId(customer), null), default);

        deleted.IsSuccess.Should().BeTrue();
        again.IsSuccess.Should().BeTrue("supprimer deux fois revient au même");
        (await Queries.QueryAsync(new ListCustomersQuery(Dossier), default)).Value.Items.Should().BeEmpty();
        (await Queries.QueryAsync(new GetCustomerQuery(Dossier, new CustomerId(customer)), default)).Value.IsArchived.Should().BeTrue();
        (await Queries.QueryAsync(new GetInvoiceQuery(Dossier, id), default)).Value.CustomerId.Should().Be(customer);
    }

    [Fact]
    public async Task Sans_fiche_entreprise_la_validation_est_refusee_sans_consommer_de_numero()
    {
        InvoiceId id = await DraftInvoiceAsync();

        Result refused = await Commands.SendAsync(new ValidateInvoiceCommand(Dossier, id, null), default);

        refused.Errors[0].Code.Should().Be(ErrorCodes.Billing.CompanyProfileMissing);
        _billing.NextNumbers.Should().BeEmpty();
        _billing.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_mention_manquante_bloque_la_validation_et_la_nomme()
    {
        InvoiceId id = await DraftInvoiceAsync();
        (await Commands.SendAsync(new SaveCompanyProfileCommand(Dossier, Cabinet with { Rcs = null }, null), default)).IsSuccess.Should().BeTrue();

        ComplianceReportResponse checklist = (await Queries.QueryAsync(new CheckInvoiceQuery(Dossier, id), default)).Value;
        Result refused = await Commands.SendAsync(new ValidateInvoiceCommand(Dossier, id, null), default);

        checklist.BlocksValidation.Should().BeTrue();
        checklist.Findings.Should().Contain(f => f.Code == ErrorCodes.Mention.SellerRcs && f.Severity == ComplianceSeverityDto.Error);
        refused.Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Billing.NotCompliant, ErrorCodes.Mention.SellerRcs);
        _billing.Invoices[id].IsDraft.Should().BeTrue();
        _billing.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task Valider_exige_la_permission_de_validation()
    {
        InvoiceId id = await DraftInvoiceAsync();
        _permissions.Granted.Remove(Permission.FactureValider);

        Result refused = await Commands.SendAsync(new ValidateInvoiceCommand(Dossier, id, null), default);

        refused.Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Le_prix_propose_vient_de_la_grille_du_client_sinon_du_catalogue()
    {
        Guid customer = (await Commands.SendAsync(new CreateCustomerCommand(Dossier, Martin), default)).Value.Value;
        CatalogItemId hour = (await Commands.SendAsync(
            new CreateCatalogItemCommand(Dossier, new CatalogItem.Draft("H", "Heure", null, CatalogItemKind.Service, "HUR", 90m, "FR-20")), default)).Value;
        CatalogItemId day = (await Commands.SendAsync(
            new CreateCatalogItemCommand(Dossier, new CatalogItem.Draft("J", "Journée", null, CatalogItemKind.Service, "DAY", 600m, "FR-20")), default)).Value;
        SavePriceListRequest grid = new() { Name = "Fidèles", CustomerIds = [customer], Entries = [new PriceListEntryRequest { CatalogItemId = hour.Value, DiscountPercent = 10m }] };
        (await Commands.SendAsync(new CreatePriceListCommand(Dossier, grid), default)).IsSuccess.Should().BeTrue();

        IReadOnlyList<ResolvedPrice> prices = (await Queries.QueryAsync(new ResolvePricesQuery(Dossier, customer, [hour.Value, day.Value, Guid.NewGuid()]), default)).Value;

        prices.Should().HaveCount(2);
        prices.Single(p => p.CatalogItemId == hour.Value).Should().BeEquivalentTo(new { UnitPriceExclTax = 90m, DiscountPercent = 10m });
        prices.Single(p => p.CatalogItemId == day.Value).Should().BeEquivalentTo(new { UnitPriceExclTax = 600m, DiscountPercent = 0m, PriceListId = (Guid?)null });
    }

    [Fact]
    public void Les_codes_de_mention_sont_des_codes_d_erreur_traduits()
    {
        string[] mentionCodes = [.. typeof(MentionCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!)];
        string[] errorCodes = [.. typeof(ErrorCodes.Mention).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!)];

        mentionCodes.Should().BeEquivalentTo(errorCodes);
    }

    [Fact]
    public void L_empreinte_canonique_ignore_l_etat_et_suit_le_contenu()
    {
        InvoiceResponse invoice = SampleInvoice();
        string hash = CanonicalDocument.ComputeHash(invoice);

        CanonicalDocument.ComputeHash(invoice with { Status = InvoiceStatusDto.Issued, Version = 9, ContentHash = hash }).Should().Be(hash);
        CanonicalDocument.ComputeHash(invoice with { Number = "FAC-000002" }).Should().NotBe(hash);
        CanonicalDocument.ComputeHash(invoice with { Totals = invoice.Totals with { TotalInclTax = 120.01m } }).Should().NotBe(hash);

        // Format v1 figé : changer l'ordre, les noms ou l'écriture des montants casse la vérification des pièces déjà scellées.
        hash.Should().Be(PinnedHash);
    }

    private const string PinnedHash = "34cef1e2a00b2f59cc622ff5655bd3f8a0cd3a0e6fdfdb3e4bfb21220ac10af7";

    private static InvoiceResponse SampleInvoice()
    {
        PartySnapshotResponse party = new("Boulangerie Martin", "1 rue de la Paix", null, "75002", "Paris", "FR", "732829320", null, null, null, null, null);
        DateTimeOffset at = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        return new InvoiceResponse(
            new Guid("0199e6a0-0000-7000-8000-000000000001"), 2, InvoiceKindDto.Invoice, InvoiceStatusDto.Validated, "FAC-000001", "FAC",
            new Guid("0199e6a0-0000-7000-8000-000000000002"), party, party with { Name = "Conseil Lettra" },
            new DateOnly(2026, 10, 15), null, new DateOnly(2026, 11, 14), null, null, VatRegimeDto.Standard, OperationCategoryDto.Services,
            null, "Mission", null, null, null, null, null, null,
            [new DocumentLineResponse(1, null, null, "Conseil", null, CatalogItemKindDto.Service, "HUR", 1m, 100m, 0m, "FR-20", 20m, VatCategoryDto.Standard, null, 100m)],
            new DocumentTotalsResponse([new VatBreakdownResponse(VatCategoryDto.Standard, 20m, 100m, 20m, null)], 100m, 20m, 120m),
            ["Indemnité forfaitaire pour frais de recouvrement en cas de retard de paiement : 40 €"],
            0m, null, Guid.Empty, Guid.Empty, null, at, at, at, null);
    }
}
