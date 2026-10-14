using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Ai.Tools.Calculations;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;

namespace Lettra.Ai.Tools.Tools;

/// <summary>Calculs comptables déterministes (port de calc.js, parité testée). Le modèle ne calcule jamais de tête.</summary>
internal sealed class CalculateTool : IAiTool
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

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => null;

    public IReadOnlySet<AssistantFeature> Features { get; } = Enum.GetValues<AssistantFeature>().ToHashSet();

    public bool ReceivesRealValues => true;

    public JsonElement InputSchema { get; } = Schema.Object(
        ("operation", Schema.String("Calcul à faire", [.. AccountingCalculator.Commands]), true),
        ("parametres", Schema.Object([.. Parameters.Select(p => (p, (JsonNode)Schema.String(p), false))]), true)).ToElement();

    public bool IsEnabledBy(AiSettings settings) => true;

    public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        Dictionary<string, string> args = input.GetProperty("parametres").EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Number
                    ? p.Value.GetDecimal().ToString(CultureInfo.InvariantCulture)
                    : p.Value.GetString() ?? string.Empty,
                StringComparer.Ordinal);
        CalcResult result = AccountingCalculator.Run(input.GetProperty("operation").GetString() ?? string.Empty, args);
        return Task.FromResult(result.IsSuccess
            ? new AiToolResult(result.ToText()) { IsDeterministicRule = true }
            : AiToolResult.Error(result.ToText()));
    }
}
