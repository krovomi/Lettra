namespace Lettra.Ai.Abstractions;

/// <summary>Réglages IA d'un cabinet. La valeur par défaut est « désactivée » (annexe F §10).</summary>
public sealed record AiSettings
{
    public bool Enabled { get; init; }
    public AiEngineKind Engine { get; init; } = AiEngineKind.None;
    public IReadOnlySet<AiFeature> Features { get; init; } = new HashSet<AiFeature>();
    public IReadOnlySet<Guid> ExcludedDossiers { get; init; } = new HashSet<Guid>();
    /// <summary>Consultation des sites officiels (annexe B §7) ; le cabinet peut la couper.</summary>
    public bool OfficialWebLookup { get; init; }
    public int MaxToolRounds { get; init; } = 8;

    public static AiSettings Disabled { get; } = new();
}

public interface IAiUsagePolicy
{
    Task<AiSettings> GetSettingsAsync(Guid cabinetId, CancellationToken ct = default);
}

public interface IAiEngineFactory
{
    /// <summary>Le moteur configuré pour ce type, ou null s'il n'est pas disponible sur ce déploiement.</summary>
    IAiEngine? Resolve(AiEngineKind kind);
}
