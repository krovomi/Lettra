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

/// <summary>
/// Registre du skill pack. <c>SkillsLastUpdated</c> : date « last_updated » du SKILL.md de chaque skill.
/// <c>ReviewedOn</c> : date de relecture du skill pack par l'expert-comptable référent.
/// </summary>
public sealed record SkillPackInfo(
    string Source,
    string Commit,
    string License,
    IReadOnlyDictionary<AiSkill, DateOnly> SkillsLastUpdated,
    DateOnly? ReviewedOn,
    string? ReviewedBy)
{
    /// <summary>Date à partir de laquelle le skill compte comme à jour : sa mise à jour, ou la relecture si elle est plus récente.</summary>
    public DateOnly FreshAsOf(AiSkill skill)
    {
        DateOnly updated = SkillsLastUpdated.TryGetValue(skill, out DateOnly date) ? date : DateOnly.MinValue;
        return ReviewedOn is { } reviewed && reviewed > updated ? reviewed : updated;
    }

    /// <summary>Le skill demande lui-même une vérification au-delà de six mois.</summary>
    public bool IsStale(AiSkill skill, DateOnly today) => today > FreshAsOf(skill).AddMonths(6);
}

/// <summary>Noms des skills, tels qu'ils figurent dans le skill pack, le registre et les paramètres d'outil.</summary>
public static class AiSkillNames
{
    public static readonly IReadOnlyList<string> All = ["comptable", "controleur-fiscal", "commissaire-aux-comptes"];

    public static string Name(AiSkill skill) => skill switch
    {
        AiSkill.ControleurFiscal => "controleur-fiscal",
        AiSkill.CommissaireAuxComptes => "commissaire-aux-comptes",
        _ => "comptable",
    };

    public static bool TryParse(string? value, out AiSkill skill)
    {
        skill = value switch
        {
            "comptable" => AiSkill.Comptable,
            "controleur-fiscal" => AiSkill.ControleurFiscal,
            "commissaire-aux-comptes" => AiSkill.CommissaireAuxComptes,
            _ => default,
        };
        return skill != default;
    }

    /// <summary>Skill par défaut quand le nom est inconnu : le skill comptable.</summary>
    public static AiSkill Parse(string? value) => TryParse(value, out AiSkill skill) ? skill : AiSkill.Comptable;
}
