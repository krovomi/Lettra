using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Ai.Abstractions;
using Lettra.Compliance.Calculations;

namespace Lettra.Ai.Tools.Tools;

/// <summary>Calculs comptables déterministes (port de calc.js). Le modèle ne calcule jamais de tête.</summary>
public sealed class CalculateTool : IAiTool
{
    private static readonly string[] Parameters =
    [
        "total", "jours-n-plus-1", "jours-totaux", "valeur", "duree", "jours-utilises", "base-jours", "resultat-fiscal",
        "taux", "taux-reduit", "taux-normal", "plafond", "jours-exercice", "tva-n-1", "montant", "jours", "base",
    ];

    public string Name => "calculer";
    public string Description =>
        "Calcul comptable exact en centimes : cca (total, jours-n-plus-1, jours-totaux) ; amortissement-lineaire (valeur, duree, " +
        "jours-utilises?, base-jours?) ; is (resultat-fiscal, puis taux OU taux-reduit/taux-normal/plafond/jours-exercice) ; " +
        "tva-acomptes-rs (tva-n-1) ; prorata (montant, jours, base?). Montants en euros (« 1200,50 »), taux en %.";
    public AiToolRisk Risk => AiToolRisk.ReadOnly;
    public string RequiredPermission => AiPermissions.UseAssistant;
    public IReadOnlySet<AiFeature> Features { get; } = new HashSet<AiFeature>
        { AiFeature.RegulatoryQuestions, AiFeature.ExplainCompliance, AiFeature.EInvoicingDiagnosis, AiFeature.DraftFromText };
    public JsonElement InputSchema { get; } = Schema.Object(
        ("operation", Schema.String("Calcul à faire", AccountingCalculator.Commands.ToArray()), true),
        ("parametres", Schema.Object(Parameters.Select(p => (p, (JsonNode)Schema.String(p), false)).ToArray()), true)).ToElement();

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default)
    {
        var args = input.GetProperty("parametres").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Number
                ? p.Value.GetDecimal().ToString(CultureInfo.InvariantCulture)
                : p.Value.GetString() ?? "");
        var result = AccountingCalculator.Run(input.GetProperty("operation").GetString()!, args);
        return Task.FromResult(result.IsSuccess
            ? new AiToolResult(result.ToText()) { IsDeterministicRule = true }
            : AiToolResult.Error(result.ToText()));
    }
}
