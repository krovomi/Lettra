using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Suppliers;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Fournisseurs, catalogue et annuaire par la même interface, en HTTP et en direct : cycle de vie complet, refus par
/// champ, et conflit de version qui rend la fiche actuelle (412 côté HTTP, même « stale » en direct).
/// </summary>
public sealed class SuppliersAndCatalogGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static string Unique(string prefix) => prefix + Guid.CreateVersion7().ToString("N")[^8..];

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_fournisseur_se_cree_se_cherche_se_modifie_puis_se_supprime(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Name = Unique("Minoterie ");

        SupplierResponse created = (await suppliers.CreateAsync(request, default)).Value!;
        OperationResult<SupplierResponse> read = await suppliers.GetAsync(created.Id, default);
        OperationResult<PagedResponse<SupplierSummary>> list = await suppliers.ListAsync(1, 200, default);
        OperationResult<PagedResponse<SupplierSummary>> found = await suppliers.SearchAsync(new PartyListQuery($"  {request.Name}  ", 1, 20), default);
        OperationResult<PagedResponse<SupplierSummary>> all = await suppliers.SearchAsync(new PartyListQuery(null, 1, 200), default);

        created.Iban.Should().Be("FR7630006000011234567890189");
        read.Value!.Name.Should().Be(request.Name);
        list.Value!.Items.Should().Contain(s => s.Id == created.Id);
        found.Value!.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
        all.Value!.Items.Should().Contain(s => s.Id == created.Id);

        request.City = "Tours";
        OperationResult<SupplierResponse> updated = await suppliers.UpdateAsync(created.Id, created.Version, request, default);
        request.City = "Blois";
        OperationResult<SupplierResponse> stale = await suppliers.UpdateAsync(created.Id, created.Version, request, default);

        updated.Value!.City.Should().Be("Tours");
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.City.Should().Be("Tours", "le refus porte la fiche actuelle, sur les deux transports");

        OperationResult<bool> staleDelete = await suppliers.DeleteAsync(created.Id, created.Version, default);
        OperationResult<bool> deleted = await suppliers.DeleteAsync(created.Id, updated.Value.Version, default);

        staleDelete.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        deleted.Value.Should().BeTrue();
        (await suppliers.GetAsync(created.Id, default)).Value!.IsArchived.Should().BeTrue("la suppression archive la fiche, gardée en base");
        (await suppliers.SearchAsync(new PartyListQuery(request.Name, 1, 20), default)).Value!.Items.Should().BeEmpty();
        (await suppliers.DeleteAsync(created.Id, null, default)).Value.Should().BeTrue("supprimer une fiche déjà archivée ne change rien");
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_fournisseur_invalide_ou_inconnu_est_refuse_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        CreateSupplierRequest request = PartiesAndCatalogApiTests.Dupuis();
        request.Siret = "73282932000075";

        OperationResult<SupplierResponse> invalid = await suppliers.CreateAsync(request, default);
        OperationResult<SupplierResponse> unknownUpdate = await suppliers.UpdateAsync(Guid.CreateVersion7(), 1, PartiesAndCatalogApiTests.Dupuis(), default);
        OperationResult<bool> unknownDelete = await suppliers.DeleteAsync(Guid.CreateVersion7(), null, default);

        invalid.Errors.Should().BeEquivalentTo(new[] { new ErrorDetail(ErrorCodes.SharedKernel.SiretInvalid, nameof(CreateSupplierRequest.Siret)) });
        unknownUpdate.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Supplier.NotFound);
        unknownDelete.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Supplier.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_article_se_cree_se_modifie_s_archive_et_se_retrouve(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ICatalogGateway catalog = services.GetRequiredService<ICatalogGateway>();
        string reference = Unique("GW-");

        OperationResult<CatalogReferenceData> data = await catalog.GetReferenceDataAsync(default);
        CatalogItemResponse item = (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(reference), default)).Value!;
        SaveCatalogItemRequest change = PartiesAndCatalogApiTests.Conseil(reference);
        change.UnitPriceExclTax = 99.5m;
        OperationResult<CatalogItemResponse> updated = await catalog.UpdateAsync(item.Id, item.Version, change, default);
        OperationResult<CatalogItemResponse> stale = await catalog.UpdateAsync(item.Id, item.Version, PartiesAndCatalogApiTests.Conseil(reference), default);
        OperationResult<CatalogItemResponse> staleArchive = await catalog.SetArchivedAsync(item.Id, item.Version, true, default);
        OperationResult<CatalogItemResponse> archived = await catalog.SetArchivedAsync(item.Id, updated.Value!.Version, true, default);

        data.Value!.UnitCodes.Should().Contain(["C62", "HUR", "LS"]);
        (await catalog.GetAsync(item.Id, default)).Value!.Reference.Should().Be(reference);
        updated.Value.UnitPriceExclTax.Should().Be(99.5m);
        stale.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
        stale.Current!.UnitPriceExclTax.Should().Be(99.5m, "le refus porte l'article actuel, sur les deux transports");
        staleArchive.Current!.Version.Should().Be(updated.Value.Version);
        archived.Value!.IsArchived.Should().BeTrue();
        (await catalog.ListAsync(new CatalogListQuery($" {reference} ", false, 1, 20), default)).Value!.TotalCount.Should().Be(0);
        (await catalog.ListAsync(new CatalogListQuery(reference, true, 1, 20), default)).Value!.Items.Should().ContainSingle().Which.Id.Should().Be(item.Id);
        (await catalog.ListAsync(new CatalogListQuery(null, true, 1, 200), default)).Value!.Items.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_article_en_double_ou_inconnu_est_refuse_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        ICatalogGateway catalog = services.GetRequiredService<ICatalogGateway>();
        string reference = Unique("DUP-");
        (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(reference), default)).IsSuccess.Should().BeTrue();
        SaveCatalogItemRequest unknownRate = PartiesAndCatalogApiTests.Conseil(Unique("TVA-"));
        unknownRate.VatRateCode = "FR-19";

        OperationResult<CatalogItemResponse> duplicate = await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil(reference.ToLowerInvariant()), default);
        OperationResult<CatalogItemResponse> rate = await catalog.CreateAsync(unknownRate, default);
        OperationResult<CatalogItemResponse> missing = await catalog.GetAsync(Guid.CreateVersion7(), default);
        OperationResult<CatalogItemResponse> missingUpdate = await catalog.UpdateAsync(Guid.CreateVersion7(), 1, PartiesAndCatalogApiTests.Conseil(Unique("X-")), default);
        OperationResult<CatalogItemResponse> missingArchive = await catalog.SetArchivedAsync(Guid.CreateVersion7(), 1, true, default);

        duplicate.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.CatalogItem.ReferenceAlreadyUsed, nameof(SaveCatalogItemRequest.Reference)));
        rate.Errors.Should().ContainSingle().Which.Should().Be(new ErrorDetail(ErrorCodes.SharedKernel.VatRateUnknown, nameof(SaveCatalogItemRequest.VatRateCode)));
        missing.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CatalogItem.NotFound);
        missingUpdate.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CatalogItem.NotFound);
        missingArchive.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.CatalogItem.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task L_annuaire_refuse_une_recherche_trop_courte_et_un_numero_de_tva_invalide(string transport)
    {
        // Seulement des refus avant tout appel à l'annuaire : aucun réseau réel en test.
        await using ServiceProvider services = _harness.Gateway(transport);
        IDirectoryGateway directory = services.GetRequiredService<IDirectoryGateway>();

        OperationResult<CompanySearchResponse> tooShort = await directory.SearchCompaniesAsync("ab", 5, default);
        OperationResult<VatNumberCheckResponse> invalid = await directory.CheckVatNumberAsync("FR 00 pas un numéro", default);

        tooShort.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.Directory.QueryTooShort);
        invalid.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.SharedKernel.VatNumberInvalid);
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        ISuppliersGateway suppliers = services.GetRequiredService<ISuppliersGateway>();
        ICatalogGateway catalog = services.GetRequiredService<ICatalogGateway>();
        IDirectoryGateway directory = services.GetRequiredService<IDirectoryGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await suppliers.CreateAsync(PartiesAndCatalogApiTests.Dupuis(), default)).Errors,
            (await suppliers.ListAsync(1, 10, default)).Errors,
            (await suppliers.SearchAsync(new PartyListQuery("dupuis", 1, 10), default)).Errors,
            (await suppliers.GetAsync(id, default)).Errors,
            (await suppliers.UpdateAsync(id, 1, PartiesAndCatalogApiTests.Dupuis(), default)).Errors,
            (await suppliers.DeleteAsync(id, 1, default)).Errors,
            (await catalog.GetReferenceDataAsync(default)).Errors,
            (await catalog.ListAsync(new CatalogListQuery("x", false, 1, 10), default)).Errors,
            (await catalog.GetAsync(id, default)).Errors,
            (await catalog.CreateAsync(PartiesAndCatalogApiTests.Conseil("X"), default)).Errors,
            (await catalog.UpdateAsync(id, 1, PartiesAndCatalogApiTests.Conseil("X"), default)).Errors,
            (await catalog.SetArchivedAsync(id, 1, true, default)).Errors,
            (await directory.SearchCompaniesAsync("martin", 5, default)).Errors,
            (await directory.CheckVatNumberAsync("FR44 732829320", default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
