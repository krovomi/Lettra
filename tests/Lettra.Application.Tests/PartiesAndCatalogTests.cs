using Lettra.Application.Abstractions.External;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

public sealed class PartiesAndCatalogTests : IAsyncLifetime
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    private readonly FakeTenantContext _tenant = new()
    {
        Descriptor = new TenantDescriptor(Dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "config:test"),
    };

    private readonly FakePermissions _permissions = new();
    private readonly InMemoryCustomers _unitOfWork = new();
    private readonly InMemoryPartiesAndCatalog _store = new();
    private readonly FakeDirectories _directories = new();
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
        services.AddSingleton<ICustomerRepository>(_unitOfWork);
        services.AddSingleton<ICustomerReadStore>(_unitOfWork);
        services.AddSingleton<IUnitOfWork>(_unitOfWork);
        services.AddPartiesAndCatalogFakes(_store, _directories);
        services.AddIntegrityFakes(new InMemoryIntegrity(Dossier));
        services.AddSingleton<Lettra.Application.Abstractions.Tenancy.IDossierProvisioner>(new RecordingDossierProvisioner());
        services.AddSingleton<Lettra.Application.Abstractions.Identity.IAuthorizationDirectory>(new EmptyAuthorizationDirectory());
        services.AddSingleton<Lettra.Application.Abstractions.Tenancy.IDossierDirectory>(new EmptyDossierDirectory());
        services.AddCabinetFakes();
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

    private static CatalogItem.Draft Item(string reference = "PRES-01", decimal price = 85m) =>
        new(reference, "Heure de conseil", null, CatalogItemKind.Service, "HUR", price, "FR-20");

    [Fact]
    public async Task Creer_un_fournisseur_puis_le_relire()
    {
        Supplier.Draft draft = new("Minoterie Dupuis", null, "73282932000074", null, null, "FR7630006000011234567890189",
            "2 chemin du Moulin", null, "45000", "Orléans", "FR");

        Result<SupplierId> created = await Commands.SendAsync(new CreateSupplierCommand(Dossier, draft), default);
        Result<Contracts.Suppliers.SupplierResponse> read = await Queries.QueryAsync(new GetSupplierQuery(Dossier, created.Value), default);

        read.Value.Siren.Should().Be("732829320");
        read.Value.Iban.Should().Be("FR7630006000011234567890189");
    }

    private async Task<SupplierId> CreateSupplierAsync() =>
        (await Commands.SendAsync(new CreateSupplierCommand(Dossier, new Supplier.Draft("Minoterie Dupuis", null, "73282932000074", null, null, null,
            "2 chemin du Moulin", null, "45000", "Orléans", "FR")), default)).Value;

    [Fact]
    public async Task Supprimer_un_fournisseur_le_retire_des_listes_mais_garde_sa_fiche()
    {
        SupplierId id = await CreateSupplierAsync();

        (await Commands.SendAsync(new DeleteSupplierCommand(Dossier, id, null), default)).IsSuccess.Should().BeTrue();

        (await Queries.QueryAsync(new ListSuppliersQuery(Dossier, 1, 50), default)).Value.TotalCount.Should().Be(0);
        (await Queries.QueryAsync(new GetSupplierQuery(Dossier, id), default)).Value.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Supprimer_deux_fois_le_meme_fournisseur_reussit_sans_rien_changer()
    {
        SupplierId id = await CreateSupplierAsync();
        await Commands.SendAsync(new DeleteSupplierCommand(Dossier, id, null), default);

        (await Commands.SendAsync(new DeleteSupplierCommand(Dossier, id, 42), default)).IsSuccess.Should().BeTrue("déjà supprimé : rien à faire, même avec une version périmée");
        _store.Suppliers[id].DomainEvents.OfType<SupplierArchiveChanged>().Should().ContainSingle();
    }

    [Fact]
    public async Task Supprimer_un_fournisseur_avec_une_version_perimee_est_refuse()
    {
        SupplierId id = await CreateSupplierAsync();

        Result deleted = await Commands.SendAsync(new DeleteSupplierCommand(Dossier, id, 42), default);

        deleted.Errors.Should().ContainSingle().Which.Code.Should().Be(ConcurrencyErrors.VersionMismatchCode);
        _store.Suppliers[id].IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Supprimer_un_fournisseur_inconnu_ou_sans_droit_est_refuse()
    {
        (await Commands.SendAsync(new DeleteSupplierCommand(Dossier, new SupplierId(Guid.CreateVersion7()), null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SupplierErrors.NotFoundCode);

        SupplierId id = await CreateSupplierAsync();
        _permissions.Granted.Remove(Permission.TiersModifier);
        (await Commands.SendAsync(new DeleteSupplierCommand(Dossier, id, null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Une_reference_deja_prise_est_refusee_sans_tenir_compte_de_la_casse()
    {
        (await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item("PRES-01")), default)).IsSuccess.Should().BeTrue();

        Result<CatalogItemId> duplicate = await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item("pres-01")), default);

        duplicate.Errors.Should().ContainSingle().Which.Should().Be(CatalogItemErrors.ReferenceAlreadyUsed);
        _store.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Modifier_un_article_en_gardant_sa_reference_est_accepte()
    {
        CatalogItemId id = (await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item()), default)).Value;

        Result updated = await Commands.SendAsync(new UpdateCatalogItemCommand(Dossier, id, Item(price: 95m)), default);

        updated.IsSuccess.Should().BeTrue();
        _store.Items[id].UnitPriceExclTax.Should().Be(95m);
    }

    [Fact]
    public async Task Renommer_un_article_vers_une_reference_prise_est_refuse()
    {
        await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item("A")), default);
        CatalogItemId b = (await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item("B")), default)).Value;

        (await Commands.SendAsync(new UpdateCatalogItemCommand(Dossier, b, Item("a")), default))
            .Errors.Should().ContainSingle().Which.Should().Be(CatalogItemErrors.ReferenceAlreadyUsed);
    }

    [Fact]
    public async Task Modifier_un_article_inconnu() =>
        (await Commands.SendAsync(new UpdateCatalogItemCommand(Dossier, CatalogItemId.New(), Item()), default))
            .Errors.Should().ContainSingle().Which.Should().Be(CatalogItemErrors.NotFound);

    [Fact]
    public async Task Un_article_archive_disparait_de_la_saisie_mais_reste_au_catalogue()
    {
        CatalogItemId id = (await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item()), default)).Value;

        (await Commands.SendAsync(new SetCatalogItemArchivedCommand(Dossier, id, true), default)).IsSuccess.Should().BeTrue();

        (await Queries.QueryAsync(new ListCatalogItemsQuery(Dossier), default)).Value.TotalCount.Should().Be(0);
        (await Queries.QueryAsync(new ListCatalogItemsQuery(Dossier, IncludeArchived: true), default)).Value.Items
            .Should().ContainSingle().Which.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task La_recherche_est_nettoyee_avant_d_atteindre_le_stockage()
    {
        await Queries.QueryAsync(new ListCatalogItemsQuery(Dossier, Search: "  conseil  "), default);
        _store.LastFilter!.Search.Should().Be("conseil");

        await Queries.QueryAsync(new ListCatalogItemsQuery(Dossier, Search: "   "), default);
        _store.LastFilter!.Search.Should().BeNull();
    }

    [Fact]
    public async Task Gerer_le_catalogue_exige_sa_permission()
    {
        _permissions.Granted.Remove(Permission.CatalogueGerer);

        (await Commands.SendAsync(new CreateCatalogItemCommand(Dossier, Item()), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(SecurityErrors.ForbiddenCode);
        (await Queries.QueryAsync(new ListCatalogItemsQuery(Dossier), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Les_listes_fermees_viennent_du_domaine()
    {
        CatalogReferenceData data = (await Queries.QueryAsync(new GetCatalogReferenceDataQuery(Dossier), default)).Value;

        data.UnitCodes.Should().Equal(UnitOfMeasure.All.Select(u => u.Code));
        data.VatRates.Select(r => r.Code).Should().Equal("FR-20", "FR-10", "FR-5.5", "FR-2.1", "FR-0");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  a ")]
    public async Task Une_recherche_trop_courte_n_interroge_pas_l_annuaire(string query)
    {
        Result<CompanySearchResponse> result = await Queries.QueryAsync(new SearchCompaniesQuery(Dossier, query), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.QueryTooShortCode);
        _directories.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task La_recherche_transmet_la_saisie_nettoyee()
    {
        await Queries.QueryAsync(new SearchCompaniesQuery(Dossier, "  Boulangerie Martin "), default);

        _directories.Searches.Should().Equal("Boulangerie Martin");
    }

    [Fact]
    public async Task Un_numero_de_tva_hors_union_n_interroge_pas_vies()
    {
        Result<VatNumberCheckResponse> result = await Queries.QueryAsync(new CheckVatNumberQuery(Dossier, "CHE123456789"), default);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DirectoryErrors.VatCountryNotSupportedCode);
        _directories.Checks.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_numero_de_tva_mal_forme_est_refuse_sous_son_champ() =>
        (await Queries.QueryAsync(new CheckVatNumberQuery(Dossier, "FR00732829320"), default))
            .Errors.Should().ContainSingle().Which.Should().Be(new Error(ErrorCodes.SharedKernel.VatNumberInvalid, ErrorKind.Validation, "VatNumber"));

    [Fact]
    public async Task Un_numero_de_tva_valide_est_verifie_aupres_de_vies()
    {
        (await Queries.QueryAsync(new CheckVatNumberQuery(Dossier, "fr 44 732829320"), default)).Value.IsValid.Should().BeTrue();

        _directories.Checks.Should().ContainSingle().Which.Value.Should().Be("FR44732829320");
    }
}
