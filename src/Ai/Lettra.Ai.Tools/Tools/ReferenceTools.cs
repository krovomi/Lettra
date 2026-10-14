using System.Text.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;

namespace Lettra.Ai.Tools.Tools;

internal static class SkillNames
{
    public static readonly string[] All = ["comptable", "controleur-fiscal", "commissaire-aux-comptes"];

    public static AiSkill Parse(string? value) => value switch
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

/// <summary>Lit une fiche <c>references/*.md</c> d'un skill (divulgation progressive, annexe B §3.2).</summary>
internal sealed class ReadReferenceTool(IAiSkillCatalog catalog) : IAiTool
{
    public string Name => "lire_reference";

    public string Description =>
        "Lit une fiche de référence métier du skill Paperasse (ex. « facturation/mentions-obligatoires.md », « tva.md »). " +
        "Utilise lister_references pour connaître les fiches. Cite le chemin de la fiche dans ta réponse.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => null;

    public IReadOnlySet<AssistantFeature> Features { get; } = Enum.GetValues<AssistantFeature>().ToHashSet();

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(
        ("skill", Schema.String("Skill à lire", SkillNames.All), true),
        ("chemin", Schema.String("Chemin de la fiche sous references/, par exemple « tva.md »"), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        AiSkill skill = SkillNames.Parse(input.GetProperty("skill").GetString());
        string path = input.GetProperty("chemin").GetString() ?? string.Empty;
        string? content = catalog.ReadReference(skill, path);
        return Task.FromResult(content is null
            ? AiToolResult.Error($"Fiche introuvable ou non autorisée : {path}. Fiches disponibles : {string.Join(", ", catalog.ListReferences(skill))}")
            : new AiToolResult(content) { Citation = $"{SkillNames.Name(skill)}/references/{path.Replace("references/", string.Empty, StringComparison.Ordinal)}" });
    }
}

internal sealed class ListReferencesTool(IAiSkillCatalog catalog) : IAiTool
{
    public string Name => "lister_references";

    public string Description => "Liste les fiches de référence métier disponibles pour un skill Paperasse.";

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => null;

    public IReadOnlySet<AssistantFeature> Features { get; } = Enum.GetValues<AssistantFeature>().ToHashSet();

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(("skill", Schema.String("Skill", SkillNames.All), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken) =>
        Task.FromResult(AiToolResult.Json(catalog.ListReferences(SkillNames.Parse(input.GetProperty("skill").GetString()))));
}
