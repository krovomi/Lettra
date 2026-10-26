using System.Net;
using System.Text.Json;
using Lettra.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lettra.Api.Tests;

/// <summary>
/// Assistant IA du poste (MCP) : un client du poste trouve seul où obtenir un jeton (RFC 9728), le 401 du point MCP le
/// lui indique, et une page web d'un autre hôte ne peut pas l'appeler.
/// </summary>
public sealed class McpClientConnectionTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string PublicRealm = "http://localhost:8081/realms/lettra";

    private static readonly Uri Mcp = new(McpClientConnection.ResourcePath, UriKind.Relative);

    /// <summary>L'API telle que la démo Docker : Keycloak joint par l'adresse interne, émetteur public dans sa découverte.</summary>
    private WebApplicationFactory<Program> Server() => factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        services.PostConfigure<JwtBearerOptions>(LettraAuthSchemes.Bearer, o =>
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { Issuer = PublicRealm }))));

    /// <summary>Un client sans jeton : la fabrique en pose un par défaut.</summary>
    private HttpClient Anonymous()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private static StringContent Initialize() => new(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
        System.Text.Encoding.UTF8,
        "application/json");

    [Theory]
    [InlineData(McpClientConnection.MetadataPath)]
    [InlineData(McpClientConnection.MetadataPath + McpClientConnection.ResourcePath)]
    public async Task Les_metadonnees_de_la_ressource_designent_Keycloak_sans_connexion(string path)
    {
        using WebApplicationFactory<Program> server = Server();
        using HttpClient anonymous = server.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        metadata.RootElement.GetProperty("resource").GetString().Should().Be("http://localhost" + McpClientConnection.ResourcePath);
        metadata.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(s => s.GetString()).Should().Equal(PublicRealm);
        metadata.RootElement.GetProperty("bearer_methods_supported").EnumerateArray().Select(s => s.GetString()).Should().Equal("header");
    }

    [Fact]
    public async Task Sans_jeton_le_point_MCP_indique_ou_lire_les_metadonnees()
    {
        using HttpClient anonymous = Anonymous();

        using HttpResponseMessage response = await anonymous.PostAsync(Mcp, Initialize());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be(
            $"Bearer resource_metadata=\"http://localhost{McpClientConnection.MetadataPath}{McpClientConnection.ResourcePath}\"");
    }

    [Fact]
    public async Task Ailleurs_le_401_reste_un_simple_Bearer()
    {
        using HttpClient anonymous = Anonymous();
        anonymous.DefaultRequestHeaders.Add(LettraAuthSchemes.CsrfHeader, "1");

        using HttpResponseMessage response = await anonymous.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Une_page_web_d_un_autre_hote_est_refusee()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", LettraApiFactory.TokenFor(LettraApiFactory.ManagerId));
        client.DefaultRequestHeaders.Add("Origin", "http://lettra.attaquant.example");

        using HttpResponseMessage response = await client.PostAsync(Mcp, Initialize());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("https://localhost")]
    public async Task Une_page_de_Lettra_elle_meme_passe_la_verification(string origin)
    {
        using HttpClient anonymous = Anonymous();
        anonymous.DefaultRequestHeaders.Add("Origin", origin);

        using HttpResponseMessage response = await anonymous.PostAsync(Mcp, Initialize());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "la vérification d'Origin laisse passer, l'authentification décide");
    }
}
