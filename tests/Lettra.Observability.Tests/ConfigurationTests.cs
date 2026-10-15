using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
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

    [Fact]
    public void La_section_Otlp_de_Lettra_active_l_export_mais_les_variables_standard_l_emportent()
    {
        ObservabilityOptions configured = new() { Otlp = new OtlpOptions { Endpoint = new Uri("http://collector:4318"), Protocol = "http/protobuf" } };
        IConfiguration empty = new ConfigurationBuilder().Build();
        IConfiguration withVariable = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DependencyInjection.OtlpEndpointVariable] = "http://collector:4317" })
            .Build();

        DependencyInjection.ExportSourceOf(empty, configured).Should().Be(OtlpExportSource.Configuration);
        DependencyInjection.ExportSourceOf(withVariable, configured).Should().Be(OtlpExportSource.Environment);
        DependencyInjection.ExportSourceOf(empty, new ObservabilityOptions { ExportEnabled = true }).Should().Be(OtlpExportSource.Environment);
    }

    [Theory]
    [InlineData("grpc", OtlpExportProtocol.Grpc)]
    [InlineData("http/protobuf", OtlpExportProtocol.HttpProtobuf)]
    [InlineData(" HTTP ", OtlpExportProtocol.HttpProtobuf)]
    public void Le_protocole_OTLP_suit_les_noms_de_la_convention(string value, OtlpExportProtocol expected) =>
        DependencyInjection.ProtocolOf(value).Should().Be(expected);

    [Fact]
    public void Un_protocole_OTLP_inconnu_est_refuse() =>
        FluentActions.Invoking(() => DependencyInjection.ProtocolOf("udp")).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Chaque_signal_porte_le_service_la_version_l_environnement_et_la_cellule()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lettra:Observability:ServiceName"] = "lettra-worker",
                ["Lettra:Observability:Cell"] = "recette",
                ["Lettra:Observability:Environment"] = "recette",
            })
            .Build();
        ServiceCollection services = new();
        services.AddLettraObservability(configuration, DeploymentMode.HostedSaaS);

        using ServiceProvider provider = services.BuildServiceProvider();
        Dictionary<string, object> attributes = provider.GetRequiredService<TracerProvider>().GetResource().Attributes
            .ToDictionary(a => a.Key, a => a.Value);

        attributes.Should().Contain("service.name", "lettra-worker")
            .And.Contain("service.namespace", "lettra")
            .And.ContainKey("service.version")
            .And.Contain(DependencyInjection.EnvironmentAttribute, "recette")
            .And.Contain(DependencyInjection.LegacyEnvironmentAttribute, "recette")
            .And.Contain(DependencyInjection.CellAttribute, "recette");
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
