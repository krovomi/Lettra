using System.Net;
using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lettra.Api.Tests;

/// <summary>
/// En-têtes de sécurité de toutes les réponses (relevés par le scan ZAP de la démonstration) : CSP compatible avec
/// Blazor WebAssembly, refus des cadres, isolation d'origine, pas de cache pour l'API et le BFF, pas d'en-tête Server.
/// </summary>
public sealed class SecurityHeadersApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static string Single(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? string.Join(", ", values)
            : string.Join(", ", response.Content.Headers.TryGetValues(name, out IEnumerable<string>? content) ? content : []);

    private static void ShouldCarrySecurityHeaders(HttpResponseMessage response)
    {
        Single(response, "X-Content-Type-Options").Should().Be("nosniff");
        Single(response, "X-Frame-Options").Should().Be("DENY");
        Single(response, "Referrer-Policy").Should().Be("strict-origin-when-cross-origin");
        Single(response, "Permissions-Policy").Should().Contain("camera=()");
        Single(response, "Cross-Origin-Opener-Policy").Should().Be("same-origin");
        Single(response, "Cross-Origin-Resource-Policy").Should().Be("same-origin");
        Single(response, "Cross-Origin-Embedder-Policy").Should().Be("require-corp");

        string csp = Single(response, "Content-Security-Policy");
        csp.Should().Contain("default-src 'self'")
            .And.Contain("script-src 'self' 'wasm-unsafe-eval'")
            .And.Contain("object-src 'none'")
            .And.Contain("frame-ancestors 'none'")
            .And.NotContain("'unsafe-eval'")
            .And.NotContain("script-src 'self' 'unsafe-inline'");
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/page-inconnue")]
    [InlineData("/bff/user")]
    public async Task Toute_reponse_porte_les_en_tetes_de_securite(string path)
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        ShouldCarrySecurityHeaders(response);
    }

    [Fact]
    public async Task Les_reponses_de_l_api_ne_sont_jamais_mises_en_cache()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldCarrySecurityHeaders(response);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Un_refus_401_porte_aussi_les_en_tetes_et_n_est_pas_mis_en_cache()
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add(LettraAuthSchemes.CsrfHeader, "1");

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/v1/customers", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldCarrySecurityHeaders(response);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public void Hors_developpement_la_csp_n_ouvre_aucune_connexion_hors_de_l_origine()
    {
        SecurityHeaders.ContentSecurityPolicy.Should().Contain("connect-src 'self';").And.NotContain("localhost");
        SecurityHeaders.DevelopmentContentSecurityPolicy.Should().Contain("ws://localhost:*");
    }

    [Fact]
    public async Task En_production_la_csp_stricte_est_servie()
    {
        using WebApplicationFactory<Program> production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using HttpClient client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Single(response, "Content-Security-Policy").Should().Be(SecurityHeaders.ContentSecurityPolicy);
    }

    [Theory]
    [InlineData("Production", "https://lettra.example", true)]
    [InlineData("Demo", "https://192.168.1.20:8443", true)]
    [InlineData("Demo", "http://localhost:8080", false)]
    [InlineData("Demo", "https://localhost:8443", false)]
    [InlineData("Production", "http://lettra.example", false)]
    [InlineData("Development", "https://lettra.example", false)]
    public async Task Hsts_n_est_servi_qu_en_https_hors_developpement_et_hors_localhost(string environment, string origin, bool expected)
    {
        using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(b => b.UseEnvironment(environment));
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin) });
        client.DefaultRequestHeaders.Authorization = null;

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        if (expected)
        {
            Single(response, "Strict-Transport-Security").Should().Be(SecurityHeaders.StrictTransportSecurity)
                .And.Contain("max-age=31536000").And.Contain("includeSubDomains");
        }
        else
        {
            response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
        }
    }
}
