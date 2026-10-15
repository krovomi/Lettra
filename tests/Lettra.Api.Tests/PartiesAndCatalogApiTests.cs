using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Directory;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Api.Tests;

/// <summary>Annuaire simulé pour l'API : jamais d'appel réseau réel en test.</summary>
internal sealed class StubDirectories : ICompanyDirectory, IVatNumberChecker
{
    public bool Down { get; set; }

    public Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CompanySearchResponse>>(Down
            ? DirectoryErrors.Unavailable
            : new CompanySearchResponse(
                [new CompanySearchResult("732829320", "73282932000074", "BOULANGERIE MARTIN", "12 RUE DE LA PAIX", null, "75002", "PARIS", "FR", "FR44732829320", "10.71C", true)],
                1));

    public Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken) =>
        Task.FromResult<Result<VatNumberCheckResponse>>(Down
            ? DirectoryErrors.Unavailable
            : new VatNumberCheckResponse(vatNumber.Value, true, "SAS BOULANGERIE MARTIN", null, DateTimeOffset.UnixEpoch));
}

public sealed class PartiesAndCatalogApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private readonly StubDirectories _directories = new();

    private HttpClient Client()
    {
        HttpClient client = factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICompanyDirectory>();
                services.RemoveAll<IVatNumberChecker>();
                services.AddSingleton<ICompanyDirectory>(_directories);
                services.AddSingleton<IVatNumberChecker>(_directories);
            }))
            .CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    internal static CreateSupplierRequest Dupuis() => new()
    {
        Name = "Minoterie Dupuis",
        Siret = "73282932000074",
        Iban = "FR76 3000 6000 0112 3456 7890 189",
        AddressLine1 = "2 chemin du Moulin",
        PostalCode = "45000",
        City = "Orléans",
    };

    internal static SaveCatalogItemRequest Conseil(string reference) => new()
    {
        Reference = reference,
        Designation = "Heure de conseil",
        Kind = CatalogItemKindDto.Service,
        UnitCode = "HUR",
        UnitPriceExclTax = 85m,
        VatRateCode = "FR-20",
    };

    private static async Task<ProblemResponse> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!;

    [Fact]
    public async Task Creer_un_fournisseur_renvoie_201()
    {
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/suppliers", Dupuis(), ContractsJsonContext.Default.CreateSupplierRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        SupplierResponse created = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SupplierResponse))!;
        created.Iban.Should().Be("FR7630006000011234567890189");
        (await client.GetFromJsonAsync("/api/v1/suppliers", ContractsJsonContext.Default.PagedResponseSupplierSummary))!
            .Items.Should().Contain(s => s.Id == created.Id);
    }

    [Fact]
    public async Task Cycle_de_vie_d_un_article_par_l_api()
    {
        using HttpClient client = Client();
        string reference = "API-" + Guid.CreateVersion7().ToString("N")[..8];

        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/catalog/items", Conseil(reference), ContractsJsonContext.Default.SaveCatalogItemRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        CatalogItemResponse item = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CatalogItemResponse))!;

        SaveCatalogItemRequest change = Conseil(reference);
        change.UnitPriceExclTax = 99.5m;
        using HttpResponseMessage updated = await client.PutVersionedAsync(
            $"/api/v1/catalog/items/{item.Id}", created.Headers.ETag!.Tag, change, ContractsJsonContext.Default.SaveCatalogItemRequest);
        CatalogItemResponse afterUpdate = (await updated.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CatalogItemResponse))!;
        afterUpdate.UnitPriceExclTax.Should().Be(99.5m);
        afterUpdate.Version.Should().Be(2);

        using HttpResponseMessage archived = await client.PutVersionedAsync(
            $"/api/v1/catalog/items/{item.Id}/archived", updated.Headers.ETag!.Tag, new SetCatalogItemArchivedRequest(true),
            ContractsJsonContext.Default.SetCatalogItemArchivedRequest);
        (await archived.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CatalogItemResponse))!.IsArchived.Should().BeTrue();

        PagedResponse<CatalogItemSummary> active = (await client.GetFromJsonAsync(
            $"/api/v1/catalog/items?search={reference}", ContractsJsonContext.Default.PagedResponseCatalogItemSummary))!;
        active.TotalCount.Should().Be(0);
        PagedResponse<CatalogItemSummary> withArchived = (await client.GetFromJsonAsync(
            $"/api/v1/catalog/items?search={reference}&includeArchived=true", ContractsJsonContext.Default.PagedResponseCatalogItemSummary))!;
        withArchived.Items.Should().ContainSingle().Which.Id.Should().Be(item.Id);
    }

    [Fact]
    public async Task Une_reference_deja_prise_renvoie_409_sous_le_champ_reference()
    {
        using HttpClient client = Client();
        string reference = "DUP-" + Guid.CreateVersion7().ToString("N")[..8];
        (await client.PostAsJsonAsync("/api/v1/catalog/items", Conseil(reference), ContractsJsonContext.Default.SaveCatalogItemRequest)).Dispose();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/catalog/items", Conseil(reference.ToLowerInvariant()), ContractsJsonContext.Default.SaveCatalogItemRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ProblemResponse problem = await ProblemAsync(response);
        problem.Code.Should().Be(ErrorCodes.CatalogItem.ReferenceAlreadyUsed);
        problem.Errors!.Should().ContainKey("Reference");
    }

    [Fact]
    public async Task Un_taux_inconnu_est_rattache_au_champ_du_contrat()
    {
        using HttpClient client = Client();
        SaveCatalogItemRequest request = Conseil("TVA-" + Guid.CreateVersion7().ToString("N")[..8]);
        request.VatRateCode = "FR-19";

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/catalog/items", request, ContractsJsonContext.Default.SaveCatalogItemRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).Errors!["VatRateCode"].Should().Equal(ErrorCodes.SharedKernel.VatRateUnknown);
    }

    [Fact]
    public async Task Les_listes_fermees_du_catalogue()
    {
        using HttpClient client = Client();

        CatalogReferenceData data = (await client.GetFromJsonAsync("/api/v1/catalog/reference-data", ContractsJsonContext.Default.CatalogReferenceData))!;

        data.UnitCodes.Should().Contain(["C62", "HUR", "LS"]);
        data.VatRates.Should().Contain(new VatRateOption("FR-5.5", 5.5m));
    }

    [Fact]
    public async Task Recherche_dans_l_annuaire_et_verification_vies()
    {
        using HttpClient client = Client();

        CompanySearchResponse found = (await client.GetFromJsonAsync("/api/v1/directory/companies?q=martin", ContractsJsonContext.Default.CompanySearchResponse))!;
        VatNumberCheckResponse vat = (await client.GetFromJsonAsync("/api/v1/directory/vat-numbers/FR44732829320", ContractsJsonContext.Default.VatNumberCheckResponse))!;

        found.Items.Should().ContainSingle().Which.Siren.Should().Be("732829320");
        vat.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Annuaire_indisponible_renvoie_503_avec_son_code()
    {
        _directories.Down = true;
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/directory/companies?q=martin", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Directory.Unavailable);
    }

    [Fact]
    public async Task Recherche_trop_courte_renvoie_400() =>
        (await Client().GetAsync(new Uri("/api/v1/directory/companies?q=ab", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
