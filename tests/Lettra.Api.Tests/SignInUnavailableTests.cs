using System.Net;
using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Gateway.Http.Device;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using static Lettra.Api.Tests.SharedServerProbes;

namespace Lettra.Api.Tests;

/// <summary>
/// Keycloak arrêté ou encore en démarrage : le navigateur revient sur la page de session, qui le dit, au lieu d'une erreur
/// 500 brute ; l'application installée dit que le serveur répond mais pas son service de connexion.
/// </summary>
public sealed class SignInUnavailableTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Realm = "http://localhost:8081/realms/lettra";
    private static readonly Uri SignIn = new("/bff/login?returnUrl=%2Ffactures", UriKind.Relative);

    [Fact]
    public async Task Keycloak_joignable_la_connexion_part_chez_lui()
    {
        using WebApplicationFactory<Program> server = WithBff(o => o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(Discovery()));

        using HttpResponseMessage response = await Browser(server).GetAsync(SignIn);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be(Realm + "/protocol/openid-connect/auth");
    }

    [Fact]
    public async Task Keycloak_arrete_avant_la_premiere_connexion_la_page_de_session_le_dit()
    {
        using WebApplicationFactory<Program> server = WithBff(o => o.ConfigurationManager = new UnreachableKeycloak());

        using HttpResponseMessage response = await Browser(server).GetAsync(SignIn);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/session?erreur=indisponible");
    }

    [Fact]
    public async Task Keycloak_arrete_apres_une_premiere_connexion_la_page_de_session_le_dit()
    {
        // La découverte du realm est déjà en cache : c'est la requête d'autorisation poussée (PAR) qui échoue.
        using WebApplicationFactory<Program> server = WithBff(o =>
        {
            OpenIdConnectConfiguration discovery = Discovery();
            discovery.PushedAuthorizationRequestEndpoint = Realm + "/protocol/openid-connect/ext/par/request";
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(discovery);
            o.Backchannel = new HttpClient(new NoAnswer());
        });

        using HttpResponseMessage response = await Browser(server).GetAsync(SignIn);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(SessionPageErrors.PathFor(SessionPageErrors.SignInUnavailable));
    }

    [Fact]
    public void Seules_une_erreur_reseau_ou_un_delai_depasse_disent_que_Keycloak_ne_repond_pas()
    {
        BffEndpoints.IsUnreachable(new HttpRequestException("refusé")).Should().BeTrue();
        BffEndpoints.IsUnreachable(UnreachableKeycloak.Failure()).Should().BeTrue();
        BffEndpoints.IsUnreachable(new TaskCanceledException("délai dépassé", new TimeoutException())).Should().BeTrue();
        BffEndpoints.IsUnreachable(new InvalidOperationException("aucun gestionnaire pour ce schéma")).Should().BeFalse();
        BffEndpoints.IsUnreachable(new TaskCanceledException("navigateur fermé")).Should().BeFalse();
    }

    [Fact]
    public async Task L_application_installee_distingue_le_serveur_qui_repond_de_sa_connexion_qui_ne_repond_pas()
    {
        using WebApplicationFactory<Program> server = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(LettraAuthSchemes.Bearer, o => o.ConfigurationManager = new UnreachableKeycloak())));
        SharedServerProbe probe = Probe(server.Server.CreateHandler());

        OperationResult<SharedServerSettings> result = await probe.CheckAsync("http://localhost:8080", CancellationToken.None);

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.SharedServer.SignInUnavailable);
    }

    private WebApplicationFactory<Program> WithBff(Action<OpenIdConnectOptions> keycloak) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => services.PostConfigure(LettraAuthSchemes.Oidc, keycloak)));

    private static HttpClient Browser(WebApplicationFactory<Program> server) =>
        server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static OpenIdConnectConfiguration Discovery() => new()
    {
        Issuer = Realm,
        AuthorizationEndpoint = Realm + "/protocol/openid-connect/auth",
        TokenEndpoint = Realm + "/protocol/openid-connect/token",
    };

    /// <summary>Keycloak arrêté : sa découverte échoue comme dans Microsoft.IdentityModel (IDX20803, qui enveloppe l'erreur réseau).</summary>
    private sealed class UnreachableKeycloak : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public static InvalidOperationException Failure() =>
            new("IDX20803: Unable to obtain configuration from: 'http://keycloak:8080/realms/lettra/.well-known/openid-configuration'.",
                new IOException("IDX20804: Unable to retrieve document.", new HttpRequestException(HttpRequestError.NameResolutionError, "Name or service not known (keycloak:8080)")));

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromException<OpenIdConnectConfiguration>(Failure());

        public void RequestRefresh()
        {
        }
    }

    /// <summary>Le conteneur Keycloak arrêté : son nom ne se résout plus sur le réseau Docker.</summary>
    private sealed class NoAnswer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.NameResolutionError, "Name or service not known (keycloak:8080)"));
    }
}
