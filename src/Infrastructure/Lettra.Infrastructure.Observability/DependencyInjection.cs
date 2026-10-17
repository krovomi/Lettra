using System.Reflection;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
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

    /// <summary>
    /// Environnement, sous le nom des conventions sémantiques actuelles (repris par Prometheus) et sous l'ancien nom,
    /// encore attendu par une partie des outils (Grafana, Tempo).
    /// </summary>
    public const string EnvironmentAttribute = "deployment.environment.name";

    /// <summary>Ancien nom de l'attribut d'environnement, émis en double.</summary>
    public const string LegacyEnvironmentAttribute = "deployment.environment";

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
                    new(EnvironmentAttribute, options.Environment),
                    new(LegacyEnvironmentAttribute, options.Environment),
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
                // Exemplaires : une mesure prise pendant une requête tracée garde l'identifiant de sa trace. Grafana
                // passe ainsi d'un pic de latence à la trace dans Tempo. Rien d'autre que trace_id et span_id n'y est ajouté.
                .SetExemplarFilter(ExemplarFilterType.TraceBased)
                .AddView(HttpServerDurationInstrument, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [.. SloBuckets.HttpServerDuration],
                })
                // Durées Lettra en secondes : mêmes bornes que HTTP (les bornes par défaut d'OpenTelemetry visent des millisecondes).
                .AddView(LettraTelemetry.UseCaseDuration.Name, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [.. SloBuckets.HttpServerDuration],
                })
                .AddView(LettraTelemetry.SyncDuration.Name, new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [.. SloBuckets.HttpServerDuration],
                }));

        // Un seul exportateur OTLP pour les trois signaux, ajouté après les processeurs de filtrage : il ne voit que
        // des signaux déjà nettoyés. Les variables OTEL_EXPORTER_OTLP_* (charts Helm) l'emportent sur Lettra:Observability:Otlp.
        switch (ExportSourceOf(configuration, options))
        {
            case OtlpExportSource.Environment:
                otel.UseOtlpExporter();
                break;
            case OtlpExportSource.Configuration:
                otel.UseOtlpExporter(ProtocolOf(options.Otlp.Protocol), options.Otlp.Endpoint!);
                break;
        }

        return services;
    }

    internal static bool IsExportEnabled(IConfiguration configuration, ObservabilityOptions options) =>
        ExportSourceOf(configuration, options) != OtlpExportSource.None;

    internal static OtlpExportSource ExportSourceOf(IConfiguration configuration, ObservabilityOptions options)
    {
        if (!string.IsNullOrWhiteSpace(configuration[OtlpEndpointVariable]))
        {
            return OtlpExportSource.Environment;
        }

        if (options.Otlp.Endpoint is not null)
        {
            return OtlpExportSource.Configuration;
        }

        // ExportEnabled seul : adresse par défaut d'OpenTelemetry (collecteur local, localhost:4317).
        return options.ExportEnabled ? OtlpExportSource.Environment : OtlpExportSource.None;
    }

    internal static OtlpExportProtocol ProtocolOf(string protocol) => protocol.Trim().ToLowerInvariant() switch
    {
        "grpc" => OtlpExportProtocol.Grpc,
        "http/protobuf" or "http" => OtlpExportProtocol.HttpProtobuf,
        _ => throw new InvalidOperationException(
            $"{ObservabilityOptions.SectionName}:Otlp:Protocol : « {protocol} » inconnu (grpc ou http/protobuf)."),
    };

    private static bool IsObserved(HttpContext context) => IsObserved(context.Request.Path);

    internal static bool IsObserved(PathString path) =>
        !UnobservedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static string VersionOf(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}

/// <summary>Origine du point de collecte OTLP.</summary>
internal enum OtlpExportSource
{
    /// <summary>Aucun : rien ne sort du processus.</summary>
    None,

    /// <summary>Variables standard <c>OTEL_EXPORTER_OTLP_*</c> (ou leurs valeurs par défaut).</summary>
    Environment,

    /// <summary>Sous-section <c>Lettra:Observability:Otlp</c>.</summary>
    Configuration,
}
