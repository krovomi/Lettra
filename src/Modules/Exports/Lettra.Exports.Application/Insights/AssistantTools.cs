using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Insights;

/// <summary>
/// Les éclairages du dossier offerts à l'assistant IA du poste (MCP) : chaque outil appelle le cas d'usage de l'écran par
/// le dispatcher, donc avec les droits de l'utilisateur, et rend sa réponse en JSON. Lecture seule ; les noms sortent
/// pseudonymisés par le serveur MCP.
/// </summary>
internal abstract class InsightTool(IQueryDispatcher queries) : IAiTool
{
    private static readonly JsonSerializerOptions Options = new(AiToolResult.JsonOptions) { Converters = { new JsonStringEnumConverter() } };

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract JsonElement InputSchema { get; }

    public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

    public Permission? RequiredPermission => Permission.DossierLire;

    public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.ExternalAssistant };

    public bool ReceivesRealValues => false;

    public bool IsEnabledBy(AiSettings settings) => true;

    public abstract Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken);

    protected static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    protected static JsonElement NoInput { get; } = Schema("""{"type":"object","properties":{},"additionalProperties":false}""");

    protected async Task<AiToolResult> AskAsync<T>(IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? new AiToolResult(JsonSerializer.Serialize(result.Value, Options)) { IsDeterministicRule = true }
            : AiToolResult.Error("Refusé : " + string.Join(", ", result.Errors.Select(e => e.Target is null ? e.Code : $"{e.Code} ({e.Target})")));
    }

    protected static string? Text(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed class PaymentWeatherTool(IQueryDispatcher queries) : InsightTool(queries)
{
    public override string Name => "meteo_des_paiements";

    public override string Description =>
        "Météo des paiements du dossier : pour chaque client, retard moyen et habituel, factures dues et date d'encaissement attendue ; " +
        "prévision des encaissements des huit prochaines semaines. Montants TTC en euros.";

    public override JsonElement InputSchema => NoInput;

    public override Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AskAsync(new GetPaymentWeatherQuery(context.Dossier), cancellationToken);
    }
}

internal sealed class OverdueTool(IQueryDispatcher queries) : InsightTool(queries)
{
    public override string Name => "factures_en_retard";

    public override string Description =>
        "Montant en retard du dossier et chaque facture échue qui le compose : total TTC, avoirs, encaissé, reste dû et jours de retard.";

    public override JsonElement InputSchema => NoInput;

    public override Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AskAsync(new ExplainFigureQuery(context.Dossier, ExplainedFigureDto.Overdue), cancellationToken);
    }
}

internal sealed class MonthRevenueTool(IQueryDispatcher queries) : InsightTool(queries)
{
    public override string Name => "chiffre_d_affaires_du_mois";

    public override string Description =>
        "Chiffre d'affaires hors taxes d'un mois et les factures, acomptes et avoirs qui le composent. Mois au format AAAA-MM ; " +
        "le mois en cours sans paramètre.";

    public override JsonElement InputSchema { get; } = Schema(
        """{"type":"object","properties":{"mois":{"type":"string","description":"Mois au format AAAA-MM"}},"additionalProperties":false}""");

    public override Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateOnly? month = null;
        if (Text(input, "mois") is { } text)
        {
            if (!DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            {
                return Task.FromResult(AiToolResult.Error("Mois attendu au format AAAA-MM."));
            }

            month = parsed;
        }

        return AskAsync(new ExplainFigureQuery(context.Dossier, ExplainedFigureDto.MonthRevenue, month), cancellationToken);
    }
}

internal sealed class DossierAsOfTool(IQueryDispatcher queries) : InsightTool(queries)
{
    public override string Name => "dossier_a_un_instant";

    public override string Description =>
        "Le dossier tel qu'il était à un instant passé, rejoué depuis le journal inaltérable : chiffre d'affaires et TVA de l'année, " +
        "factures dues, soldes des clients, dernière clôture. Instant au format ISO 8601, par exemple 2025-12-31T23:59:00+01:00.";

    public override JsonElement InputSchema { get; } = Schema(
        """{"type":"object","properties":{"instant":{"type":"string","description":"Instant ISO 8601 avec fuseau"}},"required":["instant"],"additionalProperties":false}""");

    public override Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return DateTimeOffset.TryParse(Text(input, "instant"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at)
            ? AskAsync(new GetDossierAsOfQuery(context.Dossier, at), cancellationToken)
            : Task.FromResult(AiToolResult.Error("Instant attendu au format ISO 8601."));
    }
}

internal sealed class WhatIfTool(IQueryDispatcher queries) : InsightTool(queries)
{
    private static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public override string Name => "simuler_et_si";

    public override string Description =>
        "Simulateur « et si ? », sans rien écrire : effet sur la trésorerie des treize prochaines semaines d'un délai accordé aux clients " +
        "(extraDelayDays), d'une facture à émettre (invoice : amountExclTax, vatRatePercent parmi 0, 2.1, 5.5, 10, 20, issueDate AAAA-MM-JJ, " +
        "paymentTermDays), d'un régime de TVA (vat : Current, Monthly, Quarterly, Franchise) et d'une embauche (hire : monthlyGrossSalary, " +
        "startDate, employerChargesPercent).";

    public override JsonElement InputSchema { get; } = Schema(
        """
        {"type":"object","additionalProperties":false,"properties":{
          "extraDelayDays":{"type":"integer"},
          "vat":{"type":"string","enum":["Current","Monthly","Quarterly","Franchise"]},
          "invoice":{"type":"object","additionalProperties":false,"required":["amountExclTax","vatRatePercent","issueDate","paymentTermDays"],"properties":{
            "amountExclTax":{"type":"number"},"vatRatePercent":{"type":"number"},"issueDate":{"type":"string"},"paymentTermDays":{"type":"integer"}}},
          "hire":{"type":"object","additionalProperties":false,"required":["monthlyGrossSalary","startDate","employerChargesPercent"],"properties":{
            "monthlyGrossSalary":{"type":"number"},"startDate":{"type":"string"},"employerChargesPercent":{"type":"number"}}}}}
        """);

    public override Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        WhatIfRequest? scenario;
        try
        {
            scenario = input.Deserialize<WhatIfRequest>(Read);
        }
        catch (JsonException)
        {
            scenario = null;
        }

        return scenario is null
            ? Task.FromResult(AiToolResult.Error("Scénario illisible : dates au format AAAA-MM-JJ."))
            : AskAsync(new SimulateWhatIfQuery(context.Dossier, scenario), cancellationToken);
    }
}
