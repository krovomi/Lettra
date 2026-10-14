using System.Text.Json.Serialization;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Evals;

public sealed record EvalSuite(
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("cases")] IReadOnlyList<EvalCase> Cases);

public sealed record EvalCase(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("categorie")] string Category,
    [property: JsonPropertyName("fonction")] AiFeature Feature,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("attendu")] EvalExpectations Expected,
    [property: JsonPropertyName("origine")] string? Origin = null,
    [property: JsonPropertyName("facture")] string? Invoice = null,
    [property: JsonPropertyName("contenu_importe")] string? ImportedContent = null,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions = null,
    [property: JsonPropertyName("reglages")] string? Settings = null)
{
    public bool IsBlocking => Category is "bloquant" or "securite";
}

public sealed record EvalExpectations(
    [property: JsonPropertyName("outils_appeles")] IReadOnlyList<string>? ToolsCalled = null,
    [property: JsonPropertyName("outils_non_appeles")] IReadOnlyList<string>? ToolsNotCalled = null,
    [property: JsonPropertyName("reponse_contient")] IReadOnlyList<string>? AnswerContains = null,
    [property: JsonPropertyName("reponse_ne_contient_pas")] IReadOnlyList<string>? AnswerNotContains = null,
    [property: JsonPropertyName("sources")] IReadOnlyList<string>? Citations = null,
    [property: JsonPropertyName("brouillon_cree")] bool? DraftCreated = null,
    [property: JsonPropertyName("indisponible")] bool? Unavailable = null);

public sealed record EvalResult(EvalCase Case, string Engine, IReadOnlyList<string> Failures, string Answer, int ToolRounds)
{
    public bool Passed => Failures.Count == 0;
}
