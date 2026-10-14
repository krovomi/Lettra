using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>Création d'un dossier : route d'administration du cabinet, permission vérifiée sur le cabinet.</summary>
public sealed class DossierApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Dossiers(Guid cabinet) => new($"/api/v1/cabinets/{cabinet}/dossiers", UriKind.Relative);

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid cabinet, string name) =>
        client.PostAsJsonAsync(Dossiers(cabinet), new CreateDossierRequest { Name = name }, ContractsJsonContext.Default.CreateDossierRequest);

    [Fact]
    public async Task L_administrateur_du_cabinet_cree_un_dossier_ouvert_aussitot_aux_managers()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        HttpResponseMessage response = await CreateAsync(admin, LettraApiFactory.CabinetId, "  Boulangerie du Port  ");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        DossierSummary? created = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary);
        created!.Name.Should().Be("Boulangerie du Port");

        // Le dossier s'ouvre tout de suite (base créée, migrée et estampillée) pour un manager du cabinet.
        using HttpClient client = factory.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(Contracts.Common.LettraHeaders.Dossier, created.Id.ToString());
        (await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    [InlineData("autre-cabinet")]
    [InlineData("suspendu")]
    public async Task Sans_la_permission_sur_ce_cabinet_c_est_refuse(string who)
    {
        Guid user = who switch
        {
            "manager" => LettraApiFactory.ManagerId,
            "collaborateur" => LettraApiFactory.CollaborateurId,
            "autre-cabinet" => LettraApiFactory.AutreCabinetAdminId,
            _ => LettraApiFactory.SuspenduId,
        };
        using HttpClient client = factory.ClientFor(user);

        (await CreateAsync(client, LettraApiFactory.CabinetId, "Dossier interdit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_nom_vide_est_refuse_avec_son_code()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        HttpResponseMessage response = await CreateAsync(client, LettraApiFactory.CabinetId, "   ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.Tenancy.DossierNameRequired);
    }

    [Fact]
    public async Task Sans_jeton_c_est_401()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        (await CreateAsync(client, LettraApiFactory.CabinetId, "Anonyme")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
