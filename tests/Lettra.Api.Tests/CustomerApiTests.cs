using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

public sealed class CustomerApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private HttpClient Client(Guid? dossier = null)
    {
        HttpClient client = factory.CreateClient();
        if (dossier is not null)
        {
            client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, dossier.ToString());
        }

        return client;
    }

    internal static CreateCustomerRequest Martin() => new()
    {
        Kind = CustomerKindDto.Company,
        Name = "Boulangerie Martin",
        Siret = "732 829 320 00074",
        Email = "compta@martin.fr",
        AddressLine1 = "1 rue de la Paix",
        PostalCode = "75002",
        City = "Paris",
    };

    private static async Task<ProblemResponse> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!;

    [Fact]
    public async Task Creer_un_client_renvoie_201_et_le_client()
    {
        using HttpClient client = Client(LettraApiFactory.DossierId);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/customers", Martin(), ContractsJsonContext.Default.CreateCustomerRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        CustomerResponse created = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CustomerResponse))!;
        created.Siren.Should().Be("732829320");
        response.Headers.Location!.ToString().Should().Be($"/api/v1/customers/{created.Id}");

        PagedResponse<CustomerSummary> list = (await client.GetFromJsonAsync("/api/v1/customers", ContractsJsonContext.Default.PagedResponseCustomerSummary))!;
        list.Items.Should().Contain(c => c.Id == created.Id);
    }

    [Fact]
    public async Task Une_regle_metier_renvoie_un_code_par_champ()
    {
        using HttpClient client = Client(LettraApiFactory.DossierId);
        CreateCustomerRequest request = Martin();
        request.Siret = null;

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/customers", request, ContractsJsonContext.Default.CreateCustomerRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ProblemResponse problem = await ProblemAsync(response);
        problem.Code.Should().Be(ErrorCodes.Customer.SirenRequired);
        problem.Errors.Should().ContainKey("Siren").WhoseValue.Should().Equal(ErrorCodes.Customer.SirenRequired);
    }

    [Fact]
    public async Task Les_annotations_des_contrats_sont_reevaluees_par_l_api()
    {
        using HttpClient client = Client(LettraApiFactory.DossierId);
        CreateCustomerRequest request = Martin();
        request.Name = string.Empty;

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/customers", request, ContractsJsonContext.Default.CreateCustomerRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).Errors!.Values.SelectMany(v => v).Should().Contain(ErrorCodes.Customer.NameRequired);
    }

    [Fact]
    public async Task Sans_en_tete_de_dossier_la_requete_est_refusee()
    {
        using HttpClient client = Client();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Tenancy.DossierRequired);
    }

    [Fact]
    public async Task Un_dossier_inconnu_ne_revele_rien()
    {
        using HttpClient client = Client(Guid.CreateVersion7());

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemAsync(response)).Code.Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

    [Fact]
    public async Task Le_contrat_openapi_est_publie()
    {
        using HttpClient client = Client();

        string openApi = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        openApi.Should().Contain("/api/v1/customers").And.Contain("CreateCustomer");
    }

    [Fact]
    public async Task L_application_wasm_est_servie()
    {
        using HttpClient client = Client();

        string index = await client.GetStringAsync(new Uri("/customers", UriKind.Relative));

        index.Should().Contain("blazor.webassembly.js");
    }
}
