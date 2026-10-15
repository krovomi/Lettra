using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Assistant;

/// <summary>Moteur d'IA choisi par le cabinet (ADR-0012).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssistantEngine>))]
public enum AssistantEngine
{
    /// <summary>Pas d'IA : les contrôles de conformité restent actifs.</summary>
    None = 0,

    /// <summary>Claude en région européenne, sans conservation.</summary>
    ClaudeEu = 1,

    /// <summary>Modèle à poids ouverts hébergé en France ou sur le serveur du cabinet.</summary>
    Local = 2,
}

/// <summary>D'où viennent les réglages affichés : la base Catalog, ou la configuration du déploiement tant que le cabinet n'a rien enregistré.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AiSettingsSource>))]
public enum AiSettingsSource
{
    Configuration = 1,
    Catalog = 2,
}

/// <summary>Réglages IA d'un cabinet, modifiables par son administrateur. Désactivés par défaut (annexe F §10).</summary>
public sealed record SaveAiSettingsRequest
{
    public const int MaxToolRoundsLimit = 20;

    public const int MaxExcludedDossiers = 10_000;

    public bool Enabled { get; set; }

    public AssistantEngine Engine { get; set; } = AssistantEngine.None;

    public IList<AssistantFeature> Features { get; set; } = [];

    /// <summary>Dossiers que le cabinet exclut de l'assistant (dossiers sensibles), au plus <see cref="MaxExcludedDossiers"/>.</summary>
    public IList<Guid> ExcludedDossiers { get; set; } = [];

    /// <summary>Consultation des sites officiels (annexe B §7).</summary>
    public bool OfficialWebLookup { get; set; }

    [Range(1, MaxToolRoundsLimit, ErrorMessage = ErrorCodes.Validation.Invalid)]
    public int MaxToolRounds { get; set; } = 8;

    /// <summary>Version lue avant modification ; <c>null</c> pour un premier enregistrement. Un écart renvoie <c>Ai.SettingsConflict</c>.</summary>
    public long? Version { get; set; }
}

public sealed record AiSettingsResponse(
    bool Enabled,
    AssistantEngine Engine,
    IReadOnlyList<AssistantFeature> Features,
    IReadOnlyList<Guid> ExcludedDossiers,
    bool OfficialWebLookup,
    int MaxToolRounds,
    AiSettingsSource Source,
    long? Version,
    DateTimeOffset? UpdatedAt);
