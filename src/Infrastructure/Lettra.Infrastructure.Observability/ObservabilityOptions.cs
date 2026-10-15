namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Réglages d'observabilité (section <c>Lettra:Observability</c>). Le point de collecte suit la convention
/// OpenTelemetry (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>) ou, à défaut, la sous-section
/// <c>Otlp</c> : sans l'un ni l'autre, rien n'est exporté (poste isolé, tests).
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Lettra:Observability";

    /// <summary>Nom du service (<c>lettra-api</c>, <c>lettra-worker</c>) ; par défaut, le nom de l'assembly d'entrée.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Identifiant de la cellule (<c>mut-fr-01</c>), porté par chaque signal pour le routage des alertes.</summary>
    public string Cell { get; set; } = "local";

    /// <summary>Environnement (<c>recette</c>, <c>production</c>, <c>pr-42</c>).</summary>
    public string Environment { get; set; } = "development";

    /// <summary>Force l'export même sans variable <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (vers le collecteur local par défaut).</summary>
    public bool ExportEnabled { get; set; }

    /// <summary>Point de collecte écrit dans la configuration de Lettra plutôt que dans les variables OpenTelemetry.</summary>
    public OtlpOptions Otlp { get; set; } = new();
}

/// <summary>
/// Sous-section <c>Lettra:Observability:Otlp</c>. Les variables standard <c>OTEL_EXPORTER_OTLP_*</c> l'emportent :
/// c'est par elles que les charts Helm désignent le collecteur de la cellule.
/// </summary>
public sealed class OtlpOptions
{
    /// <summary>Adresse du collecteur (<c>http://lettra-otel-collector:4317</c>) ; vide : pas d'export par cette voie.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary><c>grpc</c> (port 4317, par défaut) ou <c>http/protobuf</c> (port 4318).</summary>
    public string Protocol { get; set; } = "grpc";
}
