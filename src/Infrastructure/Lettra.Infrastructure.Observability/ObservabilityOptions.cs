namespace Lettra.Infrastructure.Observability;

/// <summary>
/// Réglages d'observabilité (section <c>Lettra:Observability</c>). Le point de collecte suit la convention
/// OpenTelemetry (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) : sans lui, rien n'est exporté (poste isolé, tests).
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
}
