using System.ComponentModel.DataAnnotations;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Alerts;

/// <summary>Échelle de gravité de l'annexe G §5 : S1 est la plus grave.</summary>
public enum AlertSeverity
{
    S1 = 1,
    S2 = 2,
    S3 = 3,
    S4 = 4,
}

public enum AlertState
{
    New = 1,
    Acknowledged = 2,
    Resolved = 3,
    Closed = 4,
}

public enum AlertScopeLevel
{
    Platform = 1,
    Cell = 2,
    Cabinet = 3,
    Group = 4,
    Dossier = 5,
    Device = 6,
}

public enum AlertCategory
{
    Availability = 1,
    Performance = 2,
    Compliance = 3,
    Security = 4,
    Functional = 5,
    Cost = 6,
}

/// <summary>
/// Une ligne du centre d'alertes (annexe G §8). Le titre est celui de la règle ; l'interface le traduit par
/// <c>Alerts.Rule.&lt;RuleId&gt;</c> quand la règle est connue. Aucune donnée de dossier : des identifiants techniques.
/// </summary>
public sealed record AlertSummary(
    Guid Id,
    string RuleId,
    string Title,
    AlertSeverity Severity,
    AlertCategory Category,
    AlertState State,
    AlertScopeLevel ScopeLevel,
    Guid? CabinetId,
    Guid? GroupId,
    Guid? DossierId,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    DateTimeOffset OpenedAt,
    int Occurrences,
    int EscalationLevel,
    string? AcknowledgedBy,
    DateTimeOffset? TakeoverDue);

/// <summary>Compteur par gravité, avec l'âge de la plus ancienne alerte que personne n'a prise en charge.</summary>
public sealed record AlertSeverityCounter(AlertSeverity Severity, int Open, DateTimeOffset? OldestUnacknowledgedSince);

/// <summary>Le centre d'alertes de l'utilisateur, filtré par ses droits, trié par gravité puis par ancienneté.</summary>
public sealed record AlertCentreResponse(IReadOnlyList<AlertSeverityCounter> Counters, IReadOnlyList<AlertSummary> Alerts);

public sealed record AlertTimelineItem(DateTimeOffset At, string Kind, string ActorKind, string ActorId, string? Reason);

public sealed record AlertDetailResponse(AlertSummary Alert, IReadOnlyList<AlertTimelineItem> Timeline);

/// <summary>Changer la gravité : motif obligatoire, tracé (annexe G §5). Mutable pour <c>EditForm</c>.</summary>
public sealed record ChangeAlertSeverityRequest
{
    public const int MaxReasonLength = 500;

    public AlertSeverity Severity { get; set; } = AlertSeverity.S2;

    [Required(ErrorMessage = ErrorCodes.Alerts.ReasonRequired)]
    [StringLength(MaxReasonLength, ErrorMessage = ErrorCodes.Validation.TooLong)]
    public string Reason { get; set; } = string.Empty;
}
