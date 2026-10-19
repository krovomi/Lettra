using System.Net;
using System.Net.Http.Headers;
using Lettra.Api.Endpoints;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Catalog.Kpi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Point d'accès des indicateurs lu par Prometheus : absent sans jeton, refusé sans le bon jeton.</summary>
public sealed class KpiScrapeTests
{
    private const string Token = "jeton-de-test-prometheus";

    [Fact]
    public async Task Sans_jeton_regle_le_point_d_acces_n_existe_pas()
    {
        await using WebApplication app = await HostAsync(token: null);

        HttpResponseMessage response = await app.GetTestClient().GetAsync(KpiEndpoints.Route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer autre-jeton")]
    [InlineData("Basic " + Token)]
    public async Task Un_appel_sans_le_bon_jeton_est_refuse(string? authorization)
    {
        await using WebApplication app = await HostAsync(Token);
        HttpClient client = app.GetTestClient();
        if (authorization is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        }

        HttpResponseMessage response = await client.GetAsync(KpiEndpoints.Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("lettra_kpi");
    }

    [Fact]
    public async Task Prometheus_lit_les_indicateurs_au_format_texte()
    {
        await using WebApplication app = await HostAsync(Token);
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        HttpResponseMessage response = await client.GetAsync(KpiEndpoints.Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        response.Content.Headers.ContentType.Parameters.Should().Contain(p => p.Name == "version" && p.Value == "0.0.4");
        (await response.Content.ReadAsStringAsync()).Should().Contain("lettra_kpi_dso_days{dossier_id=\"").And.Contain(",dossier=\"Dossier test\"} 30");
    }

    [Fact]
    public async Task Une_cellule_d_exploitation_seule_ne_publie_pas_les_indicateurs_de_gestion()
    {
        KpiSnapshot snapshot = await new FixedSnapshot().GetAsync(default);
        KpiSnapshot withOperations = snapshot with { Samples = [.. snapshot.Samples, new KpiSample(KpiMetrics.DatabaseSize, 4096)] };

        KpiEndpoints.Published(withOperations, new KpiOptions { BusinessIndicators = true }).Should().BeSameAs(withOperations);
        KpiEndpoints.Published(withOperations, new KpiOptions { BusinessIndicators = false }).Samples
            .Select(s => s.Metric).Should().Equal(KpiMetrics.DatabaseSize);
    }

    private static async Task<WebApplication> HostAsync(string? token)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<KpiOptions>(o => o.ScrapeToken = token);
        builder.Services.AddSingleton<IKpiSnapshotProvider, FixedSnapshot>();
        WebApplication app = builder.Build();
        app.MapKpiScrape();
        await app.StartAsync();
        return app;
    }

    private sealed class FixedSnapshot : IKpiSnapshotProvider
    {
        private static readonly DossierId Dossier = new(Guid.CreateVersion7());

        public Task<KpiSnapshot> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new KpiSnapshot(
            DateTimeOffset.UtcNow,
            [new KpiDossier(Dossier, "Dossier test", "Cabinet test")],
            [new KpiSample(KpiMetrics.DaysSalesOutstanding, 30, Dossier)]));
    }
}
