namespace Lettra.Ai.Abstractions;

public interface IAiSkillCatalog
{
    SkillPackInfo Info { get; }
    /// <summary>Instructions propres à Lettra, en surcouche des skills (jamais de modification du skill lui-même).</summary>
    string GetOverlay();
    string GetSkillInstructions(AiSkill skill);
    IReadOnlyList<string> ListReferences(AiSkill skill);
    /// <summary>Lit une fiche references/*.md du skill ; null si le chemin n'est pas dans la liste blanche.</summary>
    string? ReadReference(AiSkill skill, string relativePath);
}

/// <param name="SkillLastUpdated">Date « last_updated » du SKILL.md comptable.</param>
/// <param name="ReviewedOn">Date de relecture du skill pack par l'expert métier.</param>
public sealed record SkillPackInfo(
    string Source,
    string Commit,
    string License,
    DateOnly SkillLastUpdated,
    DateOnly? ReviewedOn,
    string? ReviewedBy)
{
    /// <summary>Le skill demande lui-même une vérification au-delà de 6 mois.</summary>
    public bool IsStale(DateOnly today) => today > SkillLastUpdated.AddMonths(6);
}
