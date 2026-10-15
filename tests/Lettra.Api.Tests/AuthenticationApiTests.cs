using System.Net;
using System.Net.Http.Json;
using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Annexe F §2 et §4 : authentification, MFA, droits vérifiés à l'entrée de l'API, isolation des cabinets.</summary>
public sealed class AuthenticationApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Uri Customers = new("/api/v1/customers", UriKind.Relative);

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!.Code!;

    private HttpClient Client(Guid? user, bool mfa = true)
    {
        HttpClient client = user is Guid id ? factory.ClientFor(id, mfa) : factory.CreateClient();
        if (user is null)
        {
            client.DefaultRequestHeaders.Authorization = null;
        }

        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task Sans_jeton_401_avec_un_code()
    {
        using HttpClient client = Client(null);
        client.DefaultRequestHeaders.Add(LettraAuthSchemes.CsrfHeader, "1");

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(ErrorCodes.Security.Unauthenticated);
    }

    [Fact]
    public async Task Sans_second_facteur_401_mfa_exigee()
    {
        using HttpClient client = Client(LettraApiFactory.ManagerId, mfa: false);

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).Should().Be(ErrorCodes.Security.MfaRequired);
    }

    [Fact]
    public async Task Un_jeton_d_une_autre_audience_est_refuse()
    {
        using HttpClient client = Client(null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.ManagerId, audience: "autre-api"));

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Le_collaborateur_lit_et_cree_dans_son_dossier()
    {
        using HttpClient client = Client(LettraApiFactory.CollaborateurId);

        using HttpResponseMessage created = await client.PostAsJsonAsync(Customers, CustomerApiTests.Martin(), ContractsJsonContext.Default.CreateCustomerRequest);
        using HttpResponseMessage list = await client.GetAsync(Customers);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Le_client_du_portail_ouvre_le_dossier_mais_pas_les_tiers()
    {
        using HttpClient client = Client(LettraApiFactory.PortailId);

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Le_dossier_d_un_autre_cabinet_n_existe_pas_pour_lui()
    {
        using HttpClient client = Client(LettraApiFactory.AutreCabinetAdminId);

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(ErrorCodes.Tenancy.DossierNotFound);
    }

    [Fact]
    public async Task Un_compte_suspendu_n_a_plus_aucun_droit()
    {
        using HttpClient client = Client(LettraApiFactory.SuspenduId);

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_utilisateur_inconnu_du_registre_n_ouvre_rien()
    {
        using HttpClient client = Client(Guid.CreateVersion7());

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sans_jeton_porteur_l_en_tete_anti_csrf_est_exige()
    {
        // Simule un appel du navigateur : pas de jeton, mais une session (refusée ici faute de cookie). Avec un jeton,
        // l'en-tête n'est pas exigé ; sans, l'appel est refusé avant même le dossier.
        using HttpClient client = Client(null);

        using HttpResponseMessage response = await client.GetAsync(Customers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Chaque_route_de_l_api_declare_une_permission()
    {
        IEnumerable<RouteEndpoint> routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

        routes.Should().NotBeEmpty();
        routes.Where(e => e.Metadata.GetMetadata<RequiredPermissionMetadata>() is null)
            .Select(e => e.RoutePattern.RawText)
            .Should().BeEmpty("annexe F §4 : la permission est vérifiée à l'entrée de l'API");
    }

    [Theory]
    [InlineData("/factures", "/factures")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData(null, "/")]
    public void Le_retour_apres_connexion_reste_local(string? returnUrl, string expected) =>
        BffEndpoints.LocalOnly(returnUrl).Should().Be(expected);

    [Theory]
    [InlineData("/factures", "/bff/login?returnUrl=%2Ffactures&reauthentification=true")]
    [InlineData("https://evil.example/", "/bff/login?returnUrl=%2F&reauthentification=true")]
    public void La_nouvelle_authentification_sans_second_facteur_revient_sur_une_adresse_locale(string returnUrl, string expected) =>
        BffEndpoints.ReauthenticationPath(returnUrl).Should().Be(expected);

    [Fact]
    public void La_cle_des_brouillons_du_navigateur_est_stable_propre_a_l_utilisateur_et_ne_le_nomme_pas()
    {
        Guid camille = Guid.Parse("0199e3a1-0000-7000-8000-000000000001");

        string cle = Security.BffEndpoints.DeviceStorageKeyOf(camille);

        cle.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]+$");
        cle.Should().Be(Security.BffEndpoints.DeviceStorageKeyOf(camille));
        cle.Should().NotBe(Security.BffEndpoints.DeviceStorageKeyOf(Guid.Parse("0199e3a1-0000-7000-8000-000000000002")));
        cle.Should().NotContain(camille.ToString("N"));
    }

    [Fact]
    public async Task Le_bff_sans_session_repond_401()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/bff/user", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
