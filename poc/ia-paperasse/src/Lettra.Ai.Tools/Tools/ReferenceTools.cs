using System.Text.Json;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Tools.Tools;

internal static class SkillNames
{
    public static readonly string[] All = ["comptable", "controleur-fiscal", "commissaire-aux-comptes"];

    public static AiSkill Parse(string value) => value switch
    {
        "controleur-fiscal" => AiSkill.ControleurFiscal,
        "commissaire-aux-comptes" => AiSkill.CommissaireAuxComptes,
        _ => AiSkill.Comptable,
    };

    public static string Name(AiSkill skill) => skill switch
    {
        AiSkill.ControleurFiscal => "controleur-fiscal",
        AiSkill.CommissaireAuxComptes => "commissaire-aux-comptes",
        _ => "comptable",
    };
}

/// <summary>Lit une fiche references/*.md d'un skill (divulgation progressive, annexe B §3.2).</summary>
public sealed class ReadReferenceTool(IAiSkillCatalog catalog) : IAiTool
{
    public string Name => "lire_reference";
    public string Description =>
        "Lit une fiche de référence métier du skill Paperasse (ex. « facturation/mentions-obligatoires.md », « tva.md »). " +
        "Utilise lister_references pour connaître les fiches. Cite le chemin de la fiche dans ta réponse.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.UseAssistant;
    public IReadOnlySet<AiFeature> Features { get; } = Enum.GetValues<AiFeature>().ToHashSet();
    public JsonElement InputSchema { get; } = Schema.Object(
        ("skill", Schema.String("Skill à lire", SkillNames.All), true),
        ("chemin", Schema.String("Chemin de la fiche sous references/, par exemple « tva.md »"), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        var skill = SkillNames.Parse(input.GetProperty("skill").GetString()!);
        var path = input.GetProperty("chemin").GetString()!;
        var content = catalog.ReadReference(skill, path);
        return Task.FromResult(content is null
            ? AiToolResult.Error($"Fiche introuvable ou non autorisée : {path}. Fiches disponibles : {string.Join(", ", catalog.ListReferences(skill))}")
            : new AiToolResult(content) { Citation = $"{SkillNames.Name(skill)}/references/{path.Replace("references/", "")}" });
    }
}

public sealed class ListReferencesTool(IAiSkillCatalog catalog) : IAiTool
{
    public string Name => "lister_references";
    public string Description => "Liste les fiches de référence métier disponibles pour un skill Paperasse.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.UseAssistant;
    public IReadOnlySet<AiFeature> Features { get; } = Enum.GetValues<AiFeature>().ToHashSet();
    public JsonElement InputSchema { get; } = Schema.Object(("skill", Schema.String("Skill", SkillNames.All), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default) =>
        Task.FromResult(AiToolResult.Json(catalog.ListReferences(SkillNames.Parse(input.GetProperty("skill").GetString()!))));
}
