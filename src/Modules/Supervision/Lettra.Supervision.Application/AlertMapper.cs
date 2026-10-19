using Lettra.Contracts.Alerts;
using Lettra.Supervision.Domain;
using AlertState = Lettra.Supervision.Domain.AlertState;

namespace Lettra.Supervision.Application;

/// <summary>Domaine → contrats du centre d'alertes. Rien d'autre que ce que l'alerte porte : aucune donnée de dossier.</summary>
public static class AlertMapper
{
    public static AlertSummary ToSummary(Alert alert, EscalationPolicy escalation)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(escalation);
        return new AlertSummary(
            alert.Id.Value,
            alert.RuleId,
            alert.Title,
            (AlertSeverity)alert.Severity,
            (Contracts.Alerts.AlertCategory)alert.Category,
            (Contracts.Alerts.AlertState)alert.State,
            (AlertScopeLevel)alert.Scope.Level,
            alert.Scope.Cabinet,
            alert.Scope.Group,
            alert.Scope.Dossier,
            alert.FirstSeen,
            alert.LastSeen,
            alert.OpenedAt,
            alert.Occurrences,
            alert.EscalationLevel,
            alert.AcknowledgedBy?.Id,
            alert.State == AlertState.New ? escalation.TakeoverDue(alert) : null);
    }

    /// <summary>Les alertes ouvertes d'abord, par gravité puis par ancienneté ; les compteurs ne comptent que les ouvertes.</summary>
    public static AlertCentreResponse ToCentre(IReadOnlyList<Alert> alerts, EscalationPolicy escalation)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        AlertSeverityCounter[] counters = Enum.GetValues<Severity>()
            .Select(severity =>
            {
                Alert[] open = [.. alerts.Where(a => a.Severity == severity && IsOpen(a.State))];
                DateTimeOffset? oldest = open.Where(a => a.State == AlertState.New).Min(a => (DateTimeOffset?)a.OpenedAt);
                return new AlertSeverityCounter((AlertSeverity)severity, open.Length, oldest);
            })
            .ToArray();
        AlertSummary[] rows = [.. alerts
            .OrderBy(a => IsOpen(a.State) ? 0 : 1)
            .ThenBy(a => a.Severity)
            .ThenBy(a => a.OpenedAt)
            .Select(a => ToSummary(a, escalation))];
        return new AlertCentreResponse(counters, rows);
    }

    public static AlertDetailResponse ToDetail(Alert alert, EscalationPolicy escalation)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return new AlertDetailResponse(
            ToSummary(alert, escalation),
            [.. alert.Timeline.Select(t => new AlertTimelineItem(t.At, t.Kind, t.Actor.Kind.ToString(), t.Actor.Id, t.Reason))]);
    }

    private static bool IsOpen(AlertState state) => state is AlertState.New or AlertState.Acknowledged;
}
