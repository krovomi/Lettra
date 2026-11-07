using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Dossiers;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>Création et effacement d'un dossier : routes d'administration du cabinet, permission vérifiée sur le cabinet.</summary>
public sealed class DossierApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Dossiers(Guid cabinet) => new($"/api/v1/cabinets/{cabinet}/dossiers", UriKind.Relative);

    private static Uri Erasure(Guid cabinet, Guid dossier) => new($"/api/v1/cabinets/{cabinet}/dossiers/{dossier}/effacement", UriKind.Relative);

    private static Uri Erasures(Guid cabinet) => new($"/api/v1/cabinets/{cabinet}/dossiers/effacements", UriKind.Relative);

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

    [Fact]
    public async Task L_administrateur_demande_puis_annule_l_effacement_d_un_dossier()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);
        DossierSummary created = (await (await CreateAsync(admin, LettraApiFactory.CabinetId, "Dossier à effacer")).Content
            .ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary))!;
        using HttpClient manager = factory.ClientFor(LettraApiFactory.ManagerId);
        manager.DefaultRequestHeaders.Add(Contracts.Common.LettraHeaders.Dossier, created.Id.ToString());
        Uri customers = new("/api/v1/customers", UriKind.Relative);

        // Demandé : 202, fermé aussitôt, listé avec la fin du délai de rétractation.
        HttpResponseMessage scheduled = await admin.PostAsync(Erasure(LettraApiFactory.CabinetId, created.Id), null);
        scheduled.StatusCode.Should().Be(HttpStatusCode.Accepted);
        DossierErasureResponse erasure = (await scheduled.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierErasureResponse))!;
        erasure.Id.Should().Be(created.Id);
        erasure.ErasureDueAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(29));
        (await admin.GetFromJsonAsync(Erasures(LettraApiFactory.CabinetId), ContractsJsonContext.Default.IReadOnlyListDossierErasureResponse))
            .Should().ContainSingle(e => e.Id == created.Id);
        (await manager.GetAsync(customers)).IsSuccessStatusCode.Should().BeFalse();

        // Annulé : 200, le dossier rouvre tel quel ; une seconde annulation est refusée avec son code.
        HttpResponseMessage cancelled = await admin.DeleteAsync(Erasure(LettraApiFactory.CabinetId, created.Id));
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelled.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierSummary))!.Name.Should().Be("Dossier à effacer");
        (await manager.GetAsync(customers)).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage again = await admin.DeleteAsync(Erasure(LettraApiFactory.CabinetId, created.Id));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.Tenancy.ErasureNotCancellable);
    }

    [Fact]
    public async Task Une_base_declaree_dans_la_configuration_ne_s_efface_pas()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        HttpResponseMessage response = await admin.PostAsync(Erasure(LettraApiFactory.CabinetId, LettraApiFactory.DossierId), null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.Tenancy.DossierDeclared);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    [InlineData("autre-cabinet")]
    [InlineData("suspendu")]
    public async Task Sans_la_permission_l_effacement_est_refuse(string who)
    {
        Guid user = who switch
        {
            "manager" => LettraApiFactory.ManagerId,
            "collaborateur" => LettraApiFactory.CollaborateurId,
            "autre-cabinet" => LettraApiFactory.AutreCabinetAdminId,
            _ => LettraApiFactory.SuspenduId,
        };
        using HttpClient client = factory.ClientFor(user);
        Uri erasure = Erasure(LettraApiFactory.CabinetId, LettraApiFactory.DossierId);

        (await client.PostAsync(erasure, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(erasure)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(Erasures(LettraApiFactory.CabinetId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
