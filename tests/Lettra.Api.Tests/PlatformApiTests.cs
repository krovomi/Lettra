using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Platform;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Routes de la plateforme : réservées à l'administrateur de la plateforme, qui voit les cabinets et leurs compteurs,
/// jamais leurs données. Cet hôte de test n'a ni KMS ni compte de service Keycloak : aucun cabinet n'y arrive.
/// </summary>
public sealed class PlatformApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Uri Cabinets = new("/api/v1/plateforme/cabinets", UriKind.Relative);

    private static Uri Departure(Guid cabinet) => new($"/api/v1/plateforme/cabinets/{cabinet}/depart", UriKind.Relative);

    private static OnboardCabinetRequest Request() => new()
    {
        Name = "Cabinet fictif",
        AdministratorName = "Camille Martin",
        AdministratorEmail = "camille@cabinet-fictif.example",
    };

    [Fact]
    public async Task L_administrateur_de_la_plateforme_voit_les_cabinets_et_leurs_compteurs()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.AdministrateurPlateformeId);

        PlatformOverviewResponse? overview = await client.GetFromJsonAsync(Cabinets, ContractsJsonContext.Default.PlatformOverviewResponse);

        overview!.CanOnboard.Should().BeFalse("ni KMS ni compte de service Keycloak sur cet hôte");
        PlatformCabinetResponse cabinet = overview.Cabinets.Should().ContainSingle(c => c.Id == LettraApiFactory.CabinetId).Subject;
        cabinet.Declared.Should().BeTrue();
        cabinet.State.Should().Be(PlatformCabinetStateDto.Active);
        cabinet.ActiveDossiers.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Sans_KMS_ni_Keycloak_aucun_cabinet_n_arrive_et_un_cabinet_declare_ne_part_pas()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.AdministrateurPlateformeId);

        HttpResponseMessage onboard = await client.PostAsJsonAsync(Cabinets, Request(), ContractsJsonContext.Default.OnboardCabinetRequest);
        onboard.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await onboard.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.CabinetLifecycle.Unavailable);

        HttpResponseMessage depart = await client.PostAsync(Departure(LettraApiFactory.CabinetId), null);
        depart.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await depart.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.CabinetLifecycle.CabinetDeclared);

        HttpResponseMessage cancel = await client.DeleteAsync(Departure(Guid.CreateVersion7()));
        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage retry = await client.PostAsync(new Uri("/api/v1/plateforme/dossiers/relance", UriKind.Relative), null);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        (await retry.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierRetryResponse))!.Resumed.Should().Be(0);
    }

    [Fact]
    public async Task Une_demande_d_arrivee_incomplete_est_refusee_avec_son_code()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.AdministrateurPlateformeId);
        OnboardCabinetRequest request = Request();
        request.Name = " ";

        HttpResponseMessage response = await client.PostAsJsonAsync(Cabinets, request, ContractsJsonContext.Default.OnboardCabinetRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(Contracts.Common.ErrorCodes.CabinetLifecycle.NameRequired);
    }

    [Theory]
    [InlineData("administrateur-cabinet")]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    [InlineData("suspendu")]
    public async Task Un_cabinet_n_administre_pas_la_plateforme(string who)
    {
        Guid user = who switch
        {
            "administrateur-cabinet" => LettraApiFactory.AdministrateurCabinetId,
            "manager" => LettraApiFactory.ManagerId,
            "collaborateur" => LettraApiFactory.CollaborateurId,
            _ => LettraApiFactory.SuspenduId,
        };
        using HttpClient client = factory.ClientFor(user);

        (await client.GetAsync(Cabinets)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(Cabinets, Request(), ContractsJsonContext.Default.OnboardCabinetRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(Departure(LettraApiFactory.CabinetId), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(Departure(LettraApiFactory.CabinetId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(new Uri("/api/v1/plateforme/dossiers/relance", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task L_administrateur_de_la_plateforme_n_ouvre_aucun_dossier()
    {
        using HttpClient client = factory.ClientFor(LettraApiFactory.AdministrateurPlateformeId);
        client.DefaultRequestHeaders.Add(Contracts.Common.LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        (await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative))).IsSuccessStatusCode.Should().BeFalse();
    }
}
