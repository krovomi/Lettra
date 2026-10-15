using Lettra.Domain.Common;

namespace Lettra.Supervision.Domain;

/// <summary>Identifiant d'une alerte (Guid v7).</summary>
public readonly record struct AlertId(Guid Value)
{
    public static AlertId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Ce qui s'est passé sur une alerte, dans l'ordre. Le motif est obligatoire pour un changement de gravité.</summary>
public sealed record AlertTimelineEntry(DateTimeOffset At, string Kind, Actor Actor, string? Reason = null);

/// <summary>
/// Alerte (annexe G §4) : une empreinte (règle + portée) qui dédoublonne, une gravité, une portée, une catégorie
/// et un état. Le titre est celui de la règle : jamais une donnée de dossier. Toute action est tracée ;
/// l'agent Vigie ne peut en mener aucune.
/// </summary>
public sealed class Alert : AggregateRoot<AlertId>
{
    /// <summary>Une alerte résolue sans récidive pendant ce délai est clôturée.</summary>
    public static readonly TimeSpan QuietPeriodBeforeClosing = TimeSpan.FromDays(7);

    private readonly List<AlertTimelineEntry> _timeline = [];

    private Alert(AlertId id, string ruleId, string title, Severity severity, AlertCategory category, AlertScope scope, DateTimeOffset at)
        : base(id)
    {
        RuleId = ruleId;
        Title = title;
        Severity = severity;
        InitialSeverity = severity;
        Category = category;
        Scope = scope;
        State = AlertState.New;
        FirstSeen = at;
        LastSeen = at;
        Occurrences = 1;
    }

    public string RuleId { get; }

    public string Title { get; }

    public Severity Severity { get; private set; }

    /// <summary>Gravité donnée par la règle ; la gravité courante peut en différer après un changement humain motivé.</summary>
    public Severity InitialSeverity { get; }

    public AlertCategory Category { get; }

    public AlertScope Scope { get; }

    public AlertState State { get; private set; }

    public DateTimeOffset FirstSeen { get; }

    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>Début de l'épisode en cours (premier déclenchement, ou dernière réouverture) : base des délais d'escalade.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public int Occurrences { get; private set; }

    /// <summary>Niveau d'escalade déjà atteint (0 : personne au-delà des destinataires initiaux).</summary>
    public int EscalationLevel { get; private set; }

    public Actor? AcknowledgedBy { get; private set; }

    public IReadOnlyList<AlertTimelineEntry> Timeline => _timeline;

    /// <summary>Empreinte de dédoublonnage : la même règle sur la même portée ne crée qu'une alerte.</summary>
    public string Fingerprint => FingerprintOf(RuleId, Scope);

    public static string FingerprintOf(string ruleId, AlertScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return $"{ruleId}|{scope.Key}";
    }

    /// <summary>Une règle déclenche une alerte. Seule une règle peut le faire : ni une personne, ni Vigie.</summary>
    public static Result<Alert> Raise(string ruleId, string title, Severity severity, AlertCategory category, AlertScope scope, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(ruleId) || string.IsNullOrWhiteSpace(title))
        {
            return Result<Alert>.Failure(SupervisionErrors.RuleRequired);
        }

        Alert alert = new(AlertId.New(), ruleId, title, severity, category, scope, at) { OpenedAt = at };
        alert._timeline.Add(new(at, "Raised", Actor.Rule(ruleId)));
        alert.Raise(new AlertRaised(alert.Id, severity, scope.Level, at));
        return alert;
    }

    /// <summary>Le même signal revient : on compte. Une récidive après résolution rouvre l'alerte (annexe G §8).</summary>
    public void RecordOccurrence(DateTimeOffset at)
    {
        Occurrences++;
        LastSeen = at;
        if (State is AlertState.Resolved or AlertState.Closed)
        {
            State = AlertState.New;
            OpenedAt = at;
            ResolvedAt = null;
            AcknowledgedBy = null;
            EscalationLevel = 0;
            _timeline.Add(new(at, "Reopened", Actor.Rule(RuleId)));
            Raise(new AlertRaised(Id, Severity, Scope.Level, at));
        }
    }

    public Result Acknowledge(Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Kind == ActorKind.AgentVigie)
        {
            return SupervisionErrors.AgentNotAllowed;
        }

        if (State != AlertState.New)
        {
            return SupervisionErrors.InvalidTransition;
        }

        State = AlertState.Acknowledged;
        AcknowledgedBy = actor;
        _timeline.Add(new(at, "Acknowledged", actor));
        return Result.Success();
    }

    /// <summary>Seule une personne change la gravité, avec un motif tracé. Vigie peut le suggérer, jamais l'appliquer.</summary>
    public Result ChangeSeverity(Severity severity, string reason, Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Kind == ActorKind.AgentVigie)
        {
            return SupervisionErrors.AgentNotAllowed;
        }

        if (actor.Kind != ActorKind.Human)
        {
            return SupervisionErrors.HumanRequired;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return SupervisionErrors.ReasonRequired;
        }

        if (State == AlertState.Closed)
        {
            return SupervisionErrors.InvalidTransition;
        }

        if (severity == Severity)
        {
            return SupervisionErrors.SameSeverity;
        }

        Severity = severity;
        _timeline.Add(new(at, $"SeverityChanged:{severity}", actor, reason));
        return Result.Success();
    }

    /// <summary>Escalade automatique et déterministe, appliquée par une règle (voir <see cref="EscalationPolicy"/>).</summary>
    public void Escalate(int level, string ruleId, DateTimeOffset at)
    {
        if (level <= EscalationLevel)
        {
            return;
        }

        EscalationLevel = level;
        _timeline.Add(new(at, $"Escalated:{level}", Actor.Rule(ruleId)));
    }

    /// <summary>Résolution par une personne, ou par la règle quand le signal disparaît. Jamais par Vigie.</summary>
    public Result Resolve(Actor actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Kind == ActorKind.AgentVigie)
        {
            return SupervisionErrors.AgentNotAllowed;
        }

        if (State is AlertState.Resolved or AlertState.Closed)
        {
            return SupervisionErrors.InvalidTransition;
        }

        State = AlertState.Resolved;
        ResolvedAt = at;
        _timeline.Add(new(at, "Resolved", actor));
        return Result.Success();
    }

    /// <summary>État complet d'une alerte, pour la base de supervision. Aucune donnée de dossier n'y figure.</summary>
    public sealed record Snapshot(
        Guid Id,
        string RuleId,
        string Title,
        Severity Severity,
        Severity InitialSeverity,
        AlertCategory Category,
        ScopeLevel ScopeLevel,
        string? Cell,
        Guid? Cabinet,
        Guid? Group,
        Guid? Dossier,
        Guid? Device,
        AlertState State,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen,
        DateTimeOffset OpenedAt,
        DateTimeOffset? ResolvedAt,
        int Occurrences,
        int EscalationLevel,
        Actor? AcknowledgedBy,
        IReadOnlyList<AlertTimelineEntry> Timeline);

    public Snapshot ToSnapshot() => new(
        Id.Value, RuleId, Title, Severity, InitialSeverity, Category, Scope.Level, Scope.Cell, Scope.Cabinet, Scope.Group, Scope.Dossier,
        Scope.Device, State, FirstSeen, LastSeen, OpenedAt, ResolvedAt, Occurrences, EscalationLevel, AcknowledgedBy, [.. _timeline]);

    /// <summary>Reconstitue une alerte relue de la base, sans rien rejouer ni lever d'événement.</summary>
    public static Alert Restore(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AlertScope scope = AlertScope.Restore(snapshot.ScopeLevel, snapshot.Cell, snapshot.Cabinet, snapshot.Group, snapshot.Dossier, snapshot.Device);
        Alert alert = new(new AlertId(snapshot.Id), snapshot.RuleId, snapshot.Title, snapshot.InitialSeverity, snapshot.Category, scope, snapshot.FirstSeen)
        {
            Severity = snapshot.Severity,
            State = snapshot.State,
            LastSeen = snapshot.LastSeen,
            OpenedAt = snapshot.OpenedAt,
            ResolvedAt = snapshot.ResolvedAt,
            Occurrences = snapshot.Occurrences,
            EscalationLevel = snapshot.EscalationLevel,
            AcknowledgedBy = snapshot.AcknowledgedBy,
        };
        alert._timeline.AddRange(snapshot.Timeline);
        return alert;
    }

    /// <summary>Clôture une alerte résolue restée calme sept jours. Renvoie vrai si l'alerte a été clôturée.</summary>
    public bool CloseIfQuiet(DateTimeOffset now)
    {
        if (State != AlertState.Resolved || ResolvedAt is not { } resolved || now - resolved < QuietPeriodBeforeClosing)
        {
            return false;
        }

        State = AlertState.Closed;
        _timeline.Add(new(now, "Closed", Actor.Rule(RuleId)));
        return true;
    }
}

/// <summary>Une alerte vient d'être déclenchée (ou rouverte) : le routage et les notifications suivent.</summary>
public sealed record AlertRaised(AlertId AlertId, Severity Severity, ScopeLevel ScopeLevel, DateTimeOffset OccurredAt) : IDomainEvent;
