using System.Reflection;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Branche OpenTelemetry dans un hôte Lettra (API, Worker), appelé par la composition root dans tous les modes.
/// Les signaux partent en OTLP vers le collecteur de la cellule, qui les filtre encore puis les range dans
/// Prometheus, Loki et Tempo (annexe G §3). Sans collecteur (poste isolé, tests), rien ne sort du processus.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Instrument ASP.NET Core qui porte les objectifs de service de latence.</summary>
    public const string HttpServerDurationInstrument = "http.server.request.duration";

    /// <summary>Attribut de ressource qui identifie la cellule.</summary>
    public const string CellAttribute = "lettra.cell";

    /// <summary>Variable standard OpenTelemetry : sa présence active l'export.</summary>
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private static readonly string[] UnobservedPaths = ["/health", "/metrics"];

    public static IServiceCollection AddLettraObservability(this IServiceCollection services, IConfiguration configuration, DeploymentMode mode)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(ObservabilityOptions.SectionName);
        services.Configure<ObservabilityOptions>(section);
        ObservabilityOptions options = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        string serviceName = options.ServiceName ?? Assembly.GetEntryAssembly()?.GetName().Name?.ToLowerInvariant().Replace('.', '-') ?? "lettra";

        // Le filtrage s'applique même sans export : un journal local (poste isolé) suit la même règle.
        services.AddLogging(logging => logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = false;
            o.AddProcessor(new LogRedactionProcessor());
        }));

        OpenTelemetryBuilder otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceNamespace: "lettra", serviceVersion: VersionOf(Assembly.GetEntryAssembly()))
                .AddAttributes(
                [
                    new(CellAttribute, options.Cell),
                    new("deployment.environment.name", options.Environment),
                    new(LettraTelemetry.Attributes.DeploymentMode, (int)mode),
                ]))
            .WithTracing(tracing => tracing
                .AddSource(LettraTelemetry.SourceName)
                // Groupe de méthodes, pas lambda : le délégué sur HttpContext n'est chargé que si un hôte ASP.NET Core
                // construit le traceur. Le poste MAUI (Android, iOS, macOS) n'embarque pas ASP.NET Core.
                .AddAspNetCoreInstrumentation(o => o.Filter = IsObserved)
                .AddHttpClientInstrumentation()
                .AddProcessor(new ActivityRedactionProcessor()))
            .WithMetrics(metrics => metrics
                .AddMeter(LettraTelemetry.SourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddView(HttpServerDurationInstrument, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [.. SloBuckets.HttpServerDuration],
                }));

        if (IsExportEnabled(configuration, options))
        {
            otel.UseOtlpExporter();
        }

        return services;
    }

    internal static bool IsExportEnabled(IConfiguration configuration, ObservabilityOptions options) =>
        options.ExportEnabled || !string.IsNullOrWhiteSpace(configuration[OtlpEndpointVariable]);

    private static bool IsObserved(HttpContext context) => IsObserved(context.Request.Path);

    internal static bool IsObserved(PathString path) =>
        !UnobservedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static string VersionOf(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}
