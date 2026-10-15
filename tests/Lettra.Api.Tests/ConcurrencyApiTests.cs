using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Suppliers;

namespace Lettra.Api.Tests;

/// <summary>Écriture conditionnée par la version lue (<c>If-Match</c>).</summary>
internal static class VersionedHttp
{
    public static async Task<HttpResponseMessage> PutVersionedAsync<T>(
        this HttpClient client, string uri, string? etag, T body, JsonTypeInfo<T> typeInfo)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, uri) { Content = JsonContent.Create(body, typeInfo) };
        if (etag is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        }

        return await client.SendAsync(request);
    }
}

/// <summary>Concurrence optimiste de bout en bout par HTTP (ADR-0013) : ETag, If-Match, 412 avec le document actuel, 428.</summary>
public sealed class ConcurrencyApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private HttpClient Client()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private async Task<(HttpClient Client, CatalogItemResponse Item, string ETag)> ArticleAsync()
    {
        HttpClient client = Client();
        string reference = "CC-" + Guid.NewGuid().ToString("N")[..12];
        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/v1/catalog/items", PartiesAndCatalogApiTests.Conseil(reference), ContractsJsonContext.Default.SaveCatalogItemRequest);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        CatalogItemResponse item = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CatalogItemResponse))!;
        return (client, item, created.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Un_article_cree_porte_la_version_1_en_ETag_et_dans_le_corps()
    {
        (HttpClient client, CatalogItemResponse item, string etag) = await ArticleAsync();
        using (client)
        {
            etag.Should().Be("\"1\"");
            item.Version.Should().Be(1);

            using HttpResponseMessage read = await client.GetAsync($"/api/v1/catalog/items/{item.Id}");
            read.Headers.ETag!.Tag.Should().Be("\"1\"");
            read.Headers.ETag.IsWeak.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Une_modification_sans_If_Match_est_refusee_en_428()
    {
        (HttpClient client, CatalogItemResponse item, _) = await ArticleAsync();
        using (client)
        {
            using HttpResponseMessage response = await client.PutAsJsonAsync(
                $"/api/v1/catalog/items/{item.Id}", PartiesAndCatalogApiTests.Conseil(item.Reference), ContractsJsonContext.Default.SaveCatalogItemRequest);

            response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
            (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!.Code
                .Should().Be(ErrorCodes.Concurrency.PreconditionRequired);
        }
    }

    [Fact]
    public async Task Deux_collegues_partent_de_la_meme_version_le_second_recoit_412_avec_la_version_du_premier()
    {
        (HttpClient client, CatalogItemResponse item, string etag) = await ArticleAsync();
        using (client)
        {
            SaveCatalogItemRequest premier = PartiesAndCatalogApiTests.Conseil(item.Reference) with { UnitPriceExclTax = 90m };
            SaveCatalogItemRequest second = PartiesAndCatalogApiTests.Conseil(item.Reference) with { Designation = "Heure d'audit" };

            using HttpResponseMessage ok = await client.PutVersionedAsync($"/api/v1/catalog/items/{item.Id}", etag, premier, ContractsJsonContext.Default.SaveCatalogItemRequest);
            using HttpResponseMessage stale = await client.PutVersionedAsync($"/api/v1/catalog/items/{item.Id}", etag, second, ContractsJsonContext.Default.SaveCatalogItemRequest);

            ok.StatusCode.Should().Be(HttpStatusCode.OK);
            ok.Headers.ETag!.Tag.Should().Be("\"2\"");
            stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            stale.Headers.ETag!.Tag.Should().Be("\"2\"");
            ProblemResponse problem = (await stale.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!;
            problem.Code.Should().Be(ErrorCodes.Concurrency.VersionMismatch);
            CatalogItemResponse current = problem.Current!.Value.Deserialize(ContractsJsonContext.Default.CatalogItemResponse)!;
            current.UnitPriceExclTax.Should().Be(90m, "le 412 porte la version du serveur, pour la fusion");
            current.Designation.Should().Be("Heure de conseil", "la seconde écriture n'a rien écrasé");
            current.Version.Should().Be(2);
        }
    }

    [Fact]
    public async Task Un_If_Match_illisible_ou_faible_est_refuse_en_412()
    {
        (HttpClient client, CatalogItemResponse item, _) = await ArticleAsync();
        using (client)
        {
            using HttpResponseMessage weak = await client.PutVersionedAsync(
                $"/api/v1/catalog/items/{item.Id}", null, PartiesAndCatalogApiTests.Conseil(item.Reference), ContractsJsonContext.Default.SaveCatalogItemRequest);
            weak.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

            using HttpRequestMessage request = new(HttpMethod.Put, $"/api/v1/catalog/items/{item.Id}")
            {
                Content = JsonContent.Create(PartiesAndCatalogApiTests.Conseil(item.Reference), ContractsJsonContext.Default.SaveCatalogItemRequest),
            };
            request.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");
            using HttpResponseMessage response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        }
    }

    [Fact]
    public async Task Archiver_depuis_une_version_perimee_est_refuse_en_412()
    {
        (HttpClient client, CatalogItemResponse item, _) = await ArticleAsync();
        using (client)
        {
            using HttpResponseMessage response = await client.PutVersionedAsync(
                $"/api/v1/catalog/items/{item.Id}/archived", "\"7\"", new SetCatalogItemArchivedRequest(true), ContractsJsonContext.Default.SetCatalogItemArchivedRequest);

            response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
            using HttpResponseMessage read = await client.GetAsync($"/api/v1/catalog/items/{item.Id}");
            (await read.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CatalogItemResponse))!.IsArchived.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Un_client_se_modifie_avec_If_Match_et_sa_version_avance()
    {
        using HttpClient client = Client();
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/customers", CustomerApiTests.Martin(), ContractsJsonContext.Default.CreateCustomerRequest);
        CustomerResponse customer = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!;
        CreateCustomerRequest change = CustomerApiTests.Martin() with { City = "Lyon", PostalCode = "69002" };

        using HttpResponseMessage updated = await client.PutVersionedAsync(
            $"/api/v1/customers/{customer.Id}", created.Headers.ETag!.Tag, change, ContractsJsonContext.Default.CreateCustomerRequest);
        using HttpResponseMessage stale = await client.PutVersionedAsync(
            $"/api/v1/customers/{customer.Id}", created.Headers.ETag!.Tag, change, ContractsJsonContext.Default.CreateCustomerRequest);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        CustomerResponse after = (await updated.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!;
        after.City.Should().Be("Lyon");
        after.Version.Should().Be(2);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Un_fournisseur_se_modifie_avec_If_Match()
    {
        using HttpClient client = Client();
        using HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/suppliers", PartiesAndCatalogApiTests.Dupuis(), ContractsJsonContext.Default.CreateSupplierRequest);
        SupplierResponse supplier = (await created.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SupplierResponse))!;

        using HttpResponseMessage updated = await client.PutVersionedAsync(
            $"/api/v1/suppliers/{supplier.Id}", created.Headers.ETag!.Tag, PartiesAndCatalogApiTests.Dupuis() with { Iban = null },
            ContractsJsonContext.Default.CreateSupplierRequest);

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync(ContractsJsonContext.Default.SupplierResponse))!.Iban.Should().BeNull();
    }
}
