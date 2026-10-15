using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>Skills Paperasse embarqués (annexe B §1). Les autres (fiscaliste, notaire, syndic) ne le sont pas.</summary>
public enum AiSkill
{
    Comptable = 1,
    ControleurFiscal = 2,
    CommissaireAuxComptes = 3,
}

/// <summary>Moteur choisi par le cabinet (ADR-0012).</summary>
public enum AiEngineKind
{
    /// <summary>Pas d'IA : les règles de conformité restent actives, elles sont en C#.</summary>
    None = 0,

    /// <summary>
    /// Claude en région européenne, sans conservation : fournisseur cloud dans une région UE (Amazon Bedrock, Google
    /// Vertex AI). L'API directe d'Anthropic ne garantit pas cette résidence.
    /// </summary>
    ClaudeEu = 1,

    /// <summary>Modèle à poids ouverts hébergé en France ou sur le serveur du cabinet (API compatible OpenAI).</summary>
    Local = 2,
}

/// <summary>Réglages IA d'un cabinet. La valeur par défaut est « désactivée » (annexe F §10).</summary>
public sealed record AiSettings
{
    public static AiSettings Disabled { get; } = new();

    public bool Enabled { get; init; }

    public AiEngineKind Engine { get; init; } = AiEngineKind.None;

    public IReadOnlySet<AssistantFeature> Features { get; init; } = new HashSet<AssistantFeature>();

    public IReadOnlySet<DossierId> ExcludedDossiers { get; init; } = new HashSet<DossierId>();

    /// <summary>Consultation des sites officiels (annexe B §7) ; le cabinet peut la couper.</summary>
    public bool OfficialWebLookup { get; init; }

    public int MaxToolRounds { get; init; } = 8;
}

/// <summary>Réglages IA du cabinet propriétaire du dossier (base Catalog en modes 2, 3 et 5 ; fichier local en mode 1).</summary>
public interface IAiUsagePolicy
{
    ValueTask<AiSettings> GetSettingsAsync(OrganisationId organisation, CancellationToken cancellationToken);
}

/// <summary>Moteurs disponibles sur ce déploiement.</summary>
public interface IAiEngineFactory
{
    /// <summary>Le moteur configuré pour ce type, ou <c>null</c> s'il n'est pas disponible ici.</summary>
    IAiEngine? Resolve(AiEngineKind kind);
}
