namespace Lettra.Application.Abstractions.Ai;

/// <summary>Le « skill pack » embarqué : skills Paperasse épinglés et surcouche propre à Lettra (annexe B §3).</summary>
public interface IAiSkillCatalog
{
    SkillPackInfo Info { get; }

    /// <summary>Instructions propres à Lettra, en surcouche des skills (jamais de modification du skill lui-même).</summary>
    string GetOverlay();

    string GetSkillInstructions(AiSkill skill);

    IReadOnlyList<string> ListReferences(AiSkill skill);

    /// <summary>Lit une fiche <c>references/*.md</c> du skill ; <c>null</c> si le chemin n'est pas dans la liste blanche.</summary>
    string? ReadReference(AiSkill skill, string relativePath);
}

/// <summary><c>SkillLastUpdated</c> : Date « last_updated » du SKILL.md comptable. <c>ReviewedOn</c> : Date de relecture du skill pack par l'expert-comptable référent.</summary>
public sealed record SkillPackInfo(
    string Source,
    string Commit,
    string License,
    DateOnly SkillLastUpdated,
    DateOnly? ReviewedOn,
    string? ReviewedBy)
{
    /// <summary>Le skill demande lui-même une vérification au-delà de six mois.</summary>
    public bool IsStale(DateOnly today) => today > SkillLastUpdated.AddMonths(6);
}
