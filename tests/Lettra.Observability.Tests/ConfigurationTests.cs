using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Lettra.Observability.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Les_histogrammes_HTTP_ont_une_borne_exacte_pour_chaque_objectif_de_latence() =>
        SloBuckets.HttpServerDuration.Should().Contain([SloBuckets.ReadLatencyObjectiveSeconds, SloBuckets.ValidationLatencyObjectiveSeconds])
            .And.BeInAscendingOrder();

    [Fact]
    public void Sans_collecteur_rien_n_est_exporte()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();
        IConfiguration withCollector = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DependencyInjection.OtlpEndpointVariable] = "http://collector:4317" })
            .Build();

        DependencyInjection.IsExportEnabled(empty, new ObservabilityOptions()).Should().BeFalse();
        DependencyInjection.IsExportEnabled(withCollector, new ObservabilityOptions()).Should().BeTrue();
    }

    [Theory]
    [InlineData("/health", false)]
    [InlineData("/health/ready", false)]
    [InlineData("/metrics", false)]
    [InlineData("/api/v1/customers", true)]
    public void Les_sondes_ne_polluent_pas_les_traces(string path, bool observed) =>
        DependencyInjection.IsObserved(new PathString(path)).Should().Be(observed);

    [Fact]
    public void L_enregistrement_fournit_un_traceur_dans_tous_les_modes()
    {
        ServiceCollection services = new();
        services.AddLettraObservability(new ConfigurationBuilder().Build(), DeploymentMode.IsolatedDesktop);

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetService<TracerProvider>().Should().NotBeNull();
    }
}
