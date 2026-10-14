using System.Globalization;
using System.Text.Json.Serialization;
using Lettra.Domain.Common;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Infrastructure;

/// <summary>Charge utile du webhook Alertmanager (version 4).</summary>
internal sealed record AlertmanagerPayload(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("alerts")] IReadOnlyList<AlertmanagerAlert>? Alerts);

internal sealed record AlertmanagerAlert(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels,
    [property: JsonPropertyName("annotations")] IReadOnlyDictionary<string, string>? Annotations,
    [property: JsonPropertyName("startsAt")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("endsAt")] DateTimeOffset EndsAt);

[JsonSerializable(typeof(AlertmanagerPayload))]
internal sealed partial class AlertmanagerJsonContext : JsonSerializerContext;

/// <summary>
/// Traduit une alerte Alertmanager en signal Lettra. Les règles Prometheus de la cellule (deploy/observability/rules)
/// portent les étiquettes <c>severity</c> (S1 à S4), <c>category</c>, <c>scope</c> et <c>cell</c> ; le titre vient de
/// l'annotation <c>title</c>. Une alerte mal étiquetée est refusée plutôt que devinée.
/// </summary>
internal static class AlertmanagerMapper
{
    public const string UnmappedCode = "Alert.Unmapped";

    public static Result<AlertSignal> ToSignal(AlertmanagerAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        IReadOnlyDictionary<string, string> labels = alert.Labels ?? new Dictionary<string, string>();
        IReadOnlyDictionary<string, string> annotations = alert.Annotations ?? new Dictionary<string, string>();

        if (!labels.TryGetValue("alertname", out string? rule)
            || !Enum.TryParse(labels.GetValueOrDefault("severity"), ignoreCase: true, out Severity severity)
            || !Enum.IsDefined(severity)
            || !TryCategory(labels.GetValueOrDefault("category"), out AlertCategory category))
        {
            return Result<AlertSignal>.Failure(Error.Validation(UnmappedCode));
        }

        Result<AlertScope> scope = ScopeOf(labels);
        if (scope.IsFailure)
        {
            return Result<AlertSignal>.Failure(scope.Errors);
        }

        bool firing = !string.Equals(alert.Status, "resolved", StringComparison.OrdinalIgnoreCase);
        string title = annotations.GetValueOrDefault("title") ?? rule;
        DateTimeOffset at = firing || alert.EndsAt == default ? alert.StartsAt : alert.EndsAt;
        return new AlertSignal(rule, title, severity, category, scope.Value, firing, at);
    }

    private static bool TryCategory(string? value, out AlertCategory category)
    {
        category = value switch
        {
            "availability" => AlertCategory.Availability,
            "performance" => AlertCategory.Performance,
            "compliance" => AlertCategory.Compliance,
            "security" => AlertCategory.Security,
            "functional" => AlertCategory.Functional,
            "cost" => AlertCategory.Cost,
            _ => 0,
        };
        return category != 0;
    }

    private static Result<AlertScope> ScopeOf(IReadOnlyDictionary<string, string> labels)
    {
        string? cell = labels.GetValueOrDefault("cell");
        return labels.GetValueOrDefault("scope") switch
        {
            "platform" => AlertScope.Platform(),
            "cell" => AlertScope.ForCell(cell ?? string.Empty),
            "cabinet" => AlertScope.ForCabinet(cell, GuidOf(labels, "cabinet")),
            "dossier" => AlertScope.ForDossier(cell, GuidOf(labels, "cabinet"), GuidOf(labels, "dossier")),
            _ => Result<AlertScope>.Failure(SupervisionErrors.ScopeIncomplete),
        };
    }

    private static Guid GuidOf(IReadOnlyDictionary<string, string> labels, string key) =>
        Guid.TryParse(labels.GetValueOrDefault(key), CultureInfo.InvariantCulture, out Guid value) ? value : Guid.Empty;
}
