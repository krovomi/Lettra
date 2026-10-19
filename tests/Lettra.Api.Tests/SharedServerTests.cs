using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Lettra.UI.Gateway.Http;
using Lettra.UI.Gateway.Http.Device;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lettra.Api.Tests;

/// <summary>
/// Serveur commun : l'application installée (Windows, macOS, Android, iOS) ne demande que l'adresse du serveur, lit
/// où se connecter, puis travaille sur la même base que le navigateur, par la même API et le même dossier.
/// </summary>
public sealed class SharedServerTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string PublicRealm = "http://localhost:8081/realms/lettra";

    /// <summary>L'API telle que la démo Docker : Keycloak joint par l'adresse interne, émetteur public dans sa découverte.</summary>
    private WebApplicationFactory<Program> Server() => factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        services.PostConfigure<JwtBearerOptions>(LettraAuthSchemes.Bearer, o =>
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { Issuer = PublicRealm }))));

    [Fact]
    public async Task Le_serveur_dit_sans_connexion_ou_se_connecter()
    {
        using WebApplicationFactory<Program> server = Server();
        using HttpClient anonymous = server.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri("/" + ServerDiscoveryResponse.Path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ServerDiscoveryResponse? discovery = await response.Content.ReadFromJsonAsync<ServerDiscoveryResponse>();
        discovery.Should().Be(new ServerDiscoveryResponse("Lettra", PublicRealm, "lettra-maui"));
    }

    [Fact]
    public async Task L_adresse_publique_du_realm_peut_etre_imposee()
    {
        using WebApplicationFactory<Program> server = Server().WithWebHostBuilder(b =>
            b.UseSetting("Lettra:Identity:Oidc:PublicAuthority", "https://auth.cabinet.example/realms/lettra"));
        using HttpClient anonymous = server.CreateClient();

        ServerDiscoveryResponse? discovery = await anonymous.GetFromJsonAsync<ServerDiscoveryResponse>(new Uri("/" + ServerDiscoveryResponse.Path, UriKind.Relative));

        discovery!.Authority.Should().Be("https://auth.cabinet.example/realms/lettra");
    }

    [Fact]
    public async Task L_adresse_saisie_est_verifiee_aupres_du_serveur()
    {
        using WebApplicationFactory<Program> server = Server();
        using HttpClient http = new(server.Server.CreateHandler());
        SharedServerProbe probe = new(http);

        OperationResult<SharedServerSettings> checkedServer = await probe.CheckAsync("  https://lettra.cabinet.example/session?x=1  ", CancellationToken.None);

        checkedServer.IsSuccess.Should().BeTrue();
        checkedServer.Value.Should().Be(new SharedServerSettings(new Uri("https://lettra.cabinet.example/"), new Uri(PublicRealm), "lettra-maui"));
    }

    [Fact]
    public async Task L_application_installee_voit_ce_que_le_navigateur_a_saisi_et_inversement()
    {
        using WebApplicationFactory<Program> server = Server();
        InMemoryTokenVault vault = new();
        await vault.WriteAsync(new DeviceTokens(
            LettraApiFactory.TokenFor(LettraApiFactory.ManagerId), DateTimeOffset.UtcNow.AddHours(1), null, null, LettraApiFactory.ManagerId, "Camille Démo"), CancellationToken.None);
        Guid? remembered = null;

        ServiceCollection services = new();
        services.AddSingleton<ITokenVault>(vault);
        services.AddSingleton<IAuthorizationBrowser, NoBrowser>();
        services.AddSharedServerGateways(
            new SharedServerSettings(new Uri("https://lettra.cabinet.example/"), new Uri(PublicRealm), "lettra-maui"),
            rememberedDossier: null,
            rememberDossier: d => remembered = d);
        services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = server.Server.CreateHandler()));
        await using ServiceProvider device = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Au lancement, la session vient du coffre ; le dossier ouvert est choisi sur le serveur, puis retenu.
        OperationResult<SessionResponse> session = await device.GetRequiredService<ISessionGateway>().GetAsync(CancellationToken.None);
        session.Value!.IsAuthenticated.Should().BeTrue();
        device.GetRequiredService<IDossierSelection>().CurrentDossierId.Should().Be(LettraApiFactory.DossierId);
        remembered.Should().Be(LettraApiFactory.DossierId);

        // Un client saisi dans l'application installée…
        CreateCustomerRequest request = CustomerApiTests.Martin();
        OperationResult<CustomerResponse> created = await device.GetRequiredService<ICustomersGateway>().CreateAsync(request, CancellationToken.None);
        created.IsSuccess.Should().BeTrue();

        // … est vu par le navigateur (même API, même dossier) ; et ce que le navigateur saisit est vu par l'application.
        using HttpClient browser = server.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.ManagerId));
        browser.DefaultRequestHeaders.Add("X-Dossier", LettraApiFactory.DossierId.ToString());
        string seenByBrowser = await browser.GetStringAsync(new Uri("/api/v1/customers?page=1&pageSize=200", UriKind.Relative));
        seenByBrowser.Should().Contain(created.Value!.Id.ToString());

        CreateCustomerRequest other = CustomerApiTests.Martin();
        other.Name = "Saisi dans le navigateur";
        using HttpResponseMessage fromBrowser = await browser.PostAsJsonAsync(new Uri("/api/v1/customers", UriKind.Relative), other, ContractsJsonContext.Default.CreateCustomerRequest);
        fromBrowser.StatusCode.Should().Be(HttpStatusCode.Created);
        OperationResult<PagedResponse<CustomerSummary>> seenByDevice = await device.GetRequiredService<ICustomersGateway>().ListAsync(1, 200, CancellationToken.None);
        seenByDevice.Value!.Items.Should().Contain(c => c.Name == "Saisi dans le navigateur");
    }

    [Theory]
    [InlineData("lettra.cabinet.example", "https://lettra.cabinet.example/", "http://lettra.cabinet.example/")]
    [InlineData("192.168.1.20:8443", "https://192.168.1.20:8443/", "http://192.168.1.20:8443/")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/")]
    [InlineData("HTTPS://Lettra.Example/chemin?x=1#y", "https://lettra.example/")]
    public void L_adresse_saisie_est_ramenee_a_la_racine_du_serveur(string typed, params string[] expected) =>
        SharedServerProbe.Candidates(typed).Select(u => u.ToString()).Should().Equal(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mon serveur")]
    [InlineData("ftp://lettra.example")]
    public async Task Une_saisie_qui_n_est_pas_une_adresse_est_refusee(string typed)
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ => throw new InvalidOperationException("aucun appel attendu"))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync(typed, CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.AddressInvalid);
    }

    [Fact]
    public async Task Le_http_n_est_accepte_que_vers_cet_appareil()
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ => throw new InvalidOperationException("aucun appel attendu"))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("http://192.168.1.20:8080", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.HttpsRequired);
        SharedServerProbe.IsAllowed(new Uri("http://localhost:8080/")).Should().BeTrue();
        SharedServerProbe.IsAllowed(new Uri("http://127.0.0.1:8080/")).Should().BeTrue();
    }

    [Fact]
    public async Task Une_page_de_connexion_en_clair_sur_le_reseau_est_refusee()
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ => Discovery("http://192.168.1.20:8081/realms/lettra"))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("https://192.168.1.20:8443", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.HttpsRequired);
    }

    [Fact]
    public async Task Un_autre_serveur_qu_un_serveur_Lettra_est_signale()
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("https://intranet.example", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.NotLettra);
    }

    [Fact]
    public async Task Un_certificat_non_approuve_est_signale_a_part()
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ =>
            throw new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS", new AuthenticationException("certificat inconnu")))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("https://192.168.1.20:8443", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.CertificateUntrusted);
    }

    [Fact]
    public async Task Sans_protocole_le_http_est_essaye_apres_le_https_pour_localhost()
    {
        List<Uri> asked = [];
        SharedServerProbe probe = new(new HttpClient(new Answers(request =>
        {
            asked.Add(request.RequestUri!);
            return request.RequestUri!.Scheme == "https"
                ? throw new HttpRequestException(HttpRequestError.SecureConnectionError, "port en clair")
                : Discovery(PublicRealm);
        })));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("localhost:8080", CancellationToken.None);

        result.Value!.Address.Should().Be(new Uri("http://localhost:8080/"));
        asked.Select(u => u.Scheme).Should().Equal("https", "http");
    }

    [Fact]
    public async Task Aucun_serveur_a_cette_adresse()
    {
        SharedServerProbe probe = new(new HttpClient(new Answers(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refusé"))));

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("https://192.168.1.99:8443", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.Unreachable);
    }

    [Fact]
    public async Task Le_dossier_ouvert_est_celui_du_navigateur_quand_l_utilisateur_y_a_acces()
    {
        Guid premier = Guid.CreateVersion7();
        Guid navigateur = Guid.CreateVersion7();
        SharedServerDossierSelection selection = new(null, _ => { });
        SharedServerDossierChooser chooser = new(new HttpClient(new Answers(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/dossiers" => Json($$"""[{"id":"{{premier}}","name":"Premier"},{"id":"{{navigateur}}","name":"Navigateur"}]"""),
            "/appsettings.json" => Json($$$"""{"Lettra":{"DossierId":"{{{navigateur}}}"}}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }))
        { BaseAddress = new Uri("https://lettra.example/") }, selection);

        OperationResult<Guid> chosen = await chooser.ChooseAsync(CancellationToken.None);

        chosen.Value.Should().Be(navigateur);
        selection.CurrentDossierId.Should().Be(navigateur);
    }

    [Fact]
    public async Task Sans_dossier_accessible_la_connexion_le_dit()
    {
        SharedServerDossierSelection selection = new(null, _ => { });
        SharedServerDossierChooser chooser = new(new HttpClient(new Answers(request => request.RequestUri!.AbsolutePath == "/api/v1/dossiers"
            ? Json("[]")
            : new HttpResponseMessage(HttpStatusCode.NotFound)))
        { BaseAddress = new Uri("https://lettra.example/") }, selection);

        OperationResult<Guid> chosen = await chooser.ChooseAsync(CancellationToken.None);

        chosen.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.NoDossier);
        selection.CurrentDossierId.Should().BeNull();
    }

    private static HttpResponseMessage Discovery(string authority) =>
        Json($$"""{"product":"Lettra","authority":"{{authority}}","clientId":"lettra-maui"}""");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    /// <summary>Le coffre a déjà des jetons : aucune page de connexion ne doit s'ouvrir.</summary>
    private sealed class NoBrowser : IAuthorizationBrowser
    {
        public Uri PrepareRedirectUri() => throw new InvalidOperationException("Pas de connexion interactive attendue.");

        public Task<Uri?> AuthorizeAsync(Uri authorizeUrl, Uri redirectUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Pas de connexion interactive attendue.");
    }
}
