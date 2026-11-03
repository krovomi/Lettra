using Lettra.Domain.Common;

namespace Lettra.Domain.Decisions;

/// <summary>Identifiant d'une décision à prendre. Guid v7 : triable, sans collision entre postes hors ligne.</summary>
public readonly record struct DecisionId(Guid Value)
{
    public static DecisionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Où en est une décision proposée par une règle.</summary>
public enum DecisionStatus
{
    /// <summary>À décider : elle est dans la liste.</summary>
    Proposed = 1,

    /// <summary>« Plus tard » : elle revient le jour choisi si la règle la propose encore.</summary>
    Snoozed = 2,

    /// <summary>Acceptée : l'action préparée a été faite.</summary>
    Accepted = 3,

    /// <summary>Ignorée : elle n'est plus proposée tant que la règle propose la même chose.</summary>
    Dismissed = 4,

    /// <summary>Réglée d'elle-même : la règle ne la propose plus (la facture est payée, l'échéance est déposée…).</summary>
    Resolved = 5,
}

/// <summary>Pour qui est la décision : le cabinet, l'entreprise cliente, ou le client final de l'entreprise.</summary>
public enum DecisionAudience
{
    Cabinet = 1,
    Entreprise = 2,
    ClientFinal = 3,
}

/// <summary>Urgence d'une décision : décide de l'ordre et de la couleur.</summary>
public enum DecisionUrgency
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

/// <summary>L'action qu'une décision prépare : un type et sa charge utile JSON, exécutés par le gestionnaire du même type.</summary>
public sealed record DecisionAction(string Kind, string Payload);

/// <summary>
/// Ce qu'une règle propose. <see cref="Kind"/> + <see cref="Subject"/> est la clé : une même décision n'est proposée
/// qu'une fois. Le sujet porte l'occurrence (la facture et le niveau de relance, l'échéance et sa période…), si bien
/// qu'une nouvelle occurrence est une nouvelle décision. <see cref="Facts"/> remplit les phrases FR et EN de l'écran.
/// </summary>
public sealed record DecisionProposal(
    string Kind,
    string Subject,
    DecisionAudience Audience,
    DecisionUrgency Urgency,
    decimal? Impact,
    DateOnly? DueOn,
    IReadOnlyDictionary<string, string> Facts,
    string Link,
    DecisionAction? Action = null);

/// <summary>
/// Une décision à prendre, dans la base du dossier. Une règle déterministe la propose à chaque actualisation ; elle reste
/// tant que la règle la propose, revient après « Plus tard », et se règle d'elle-même quand la règle ne la propose plus.
/// Acceptée ou ignorée, elle n'est plus reproposée. Aucune IA ne décide ni n'écrit ici.
/// </summary>
public sealed class Decision : AggregateRoot<DecisionId>, IVersioned
{
    public const int MaxKindLength = 64;
    public const int MaxSubjectLength = 160;
    public const int MaxLinkLength = 200;
    public const int MaxFacts = 12;
    public const int MaxFactKeyLength = 40;
    public const int MaxFactValueLength = 200;
    public const int MaxPayloadLength = 2000;

    /// <summary>« Plus tard » : au plus un an.</summary>
    public const int MaxSnoozeDays = 366;

    /// <summary>Une décision close (acceptée, ignorée, réglée) est effacée après ce délai.</summary>
    public const int RetentionMonths = 13;

    // Constructeur réservé à la persistance.
    private Decision(DecisionId id)
        : base(id)
    {
    }

    public string Kind { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public DecisionAudience Audience { get; private set; }

    public DecisionUrgency Urgency { get; private set; }

    /// <summary>L'effet chiffré en euros, s'il y en a un (la phrase dit HT ou TTC).</summary>
    public decimal? Impact { get; private set; }

    /// <summary>Avant quand décider.</summary>
    public DateOnly? DueOn { get; private set; }

    public IReadOnlyDictionary<string, string> Facts { get; private set; } = new Dictionary<string, string>();

    /// <summary>L'écran qui montre le détail, relatif à la base de l'application (« reminders »).</summary>
    public string Link { get; private set; } = string.Empty;

    public string? ActionKind { get; private set; }

    public string? ActionPayload { get; private set; }

    /// <summary>La version de la règle qui l'a proposée la dernière fois, gardée avec la décision.</summary>
    public int RuleVersion { get; private set; }

    public DecisionStatus Status { get; private set; }

    public DateOnly? SnoozedUntil { get; private set; }

    public DateTimeOffset ProposedAt { get; private set; }

    /// <summary>La dernière actualisation où la règle l'a proposée.</summary>
    public DateTimeOffset SeenAt { get; private set; }

    /// <summary>Acceptée, ignorée ou réglée : quand.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Qui l'a acceptée, ignorée ou remise à plus tard ; vide quand elle s'est réglée d'elle-même.</summary>
    public Guid? DecidedBy { get; private set; }

    public long Version { get; private set; }

    public bool HasAction => ActionKind is not null;

    /// <summary>À décider ou remise à plus tard : la règle la gère encore.</summary>
    public bool IsOpen => Status is DecisionStatus.Proposed or DecisionStatus.Snoozed;

    public static Decision Propose(DecisionProposal proposal, int ruleVersion, DateTimeOffset now)
    {
        Validate(proposal);
        Decision decision = new(DecisionId.New())
        {
            Kind = proposal.Kind,
            Subject = proposal.Subject,
            Status = DecisionStatus.Proposed,
            ProposedAt = now,
        };
        decision.Apply(proposal, ruleVersion, now);
        return decision;
    }

    /// <summary>
    /// La règle la propose encore : les faits sont mis à jour. « Plus tard » échu ou réglée entre-temps, elle revient ;
    /// acceptée ou ignorée, elle reste close.
    /// </summary>
    public void Refresh(DecisionProposal proposal, int ruleVersion, DateOnly today, DateTimeOffset now)
    {
        Validate(proposal);
        if (!string.Equals(proposal.Kind, Kind, StringComparison.Ordinal) || !string.Equals(proposal.Subject, Subject, StringComparison.Ordinal))
        {
            throw new ArgumentException("La proposition porte sur une autre décision.", nameof(proposal));
        }

        if (Status is DecisionStatus.Accepted or DecisionStatus.Dismissed)
        {
            return;
        }

        if (Status == DecisionStatus.Resolved || (Status == DecisionStatus.Snoozed && SnoozedUntil <= today))
        {
            Reopen();
        }

        Apply(proposal, ruleVersion, now);
    }

    /// <summary>La règle ne la propose plus : ouverte, elle est réglée d'elle-même.</summary>
    public void Resolve(DateTimeOffset now)
    {
        if (IsOpen)
        {
            Close(DecisionStatus.Resolved, null, now);
        }
    }

    /// <summary>Acceptée par un utilisateur, une fois son action faite.</summary>
    public Result Accept(Guid user, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return DecisionErrors.NotOpen;
        }

        Close(DecisionStatus.Accepted, user, now);
        return Result.Success();
    }

    public Result Dismiss(Guid user, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return DecisionErrors.NotOpen;
        }

        Close(DecisionStatus.Dismissed, user, now);
        return Result.Success();
    }

    /// <summary>« Plus tard » : elle quitte la liste jusqu'au jour choisi (le lendemain au plus tôt, dans un an au plus tard).</summary>
    public Result Snooze(DateOnly until, DateOnly today, Guid user)
    {
        if (!IsOpen)
        {
            return DecisionErrors.NotOpen;
        }

        if (until <= today || until > today.AddDays(MaxSnoozeDays))
        {
            return DecisionErrors.SnoozeDateInvalid;
        }

        Status = DecisionStatus.Snoozed;
        SnoozedUntil = until;
        DecidedBy = user;
        return Result.Success();
    }

    /// <summary>Remettre dans la liste une décision ignorée ou remise à plus tard (une erreur de clic se répare).</summary>
    public Result Restore()
    {
        if (Status is not (DecisionStatus.Dismissed or DecisionStatus.Snoozed))
        {
            return DecisionErrors.NotRestorable;
        }

        Reopen();
        return Result.Success();
    }

    /// <summary>L'état vu le jour donné : « Plus tard » échu compte comme à décider, même avant l'actualisation suivante.</summary>
    public DecisionStatus StatusOn(DateOnly today) =>
        Status == DecisionStatus.Snoozed && SnoozedUntil <= today ? DecisionStatus.Proposed : Status;

    /// <summary>Close depuis plus de <see cref="RetentionMonths"/> mois : à effacer.</summary>
    public bool IsExpired(DateTimeOffset now) => ClosedAt is { } closed && closed < now.AddMonths(-RetentionMonths);

    private void Reopen()
    {
        Status = DecisionStatus.Proposed;
        SnoozedUntil = null;
        ClosedAt = null;
        DecidedBy = null;
    }

    private void Close(DecisionStatus status, Guid? user, DateTimeOffset now)
    {
        Status = status;
        SnoozedUntil = null;
        ClosedAt = now;
        DecidedBy = user;
    }

    private void Apply(DecisionProposal proposal, int ruleVersion, DateTimeOffset now)
    {
        Audience = proposal.Audience;
        Urgency = proposal.Urgency;
        Impact = proposal.Impact;
        DueOn = proposal.DueOn;
        Facts = new Dictionary<string, string>(proposal.Facts, StringComparer.Ordinal);
        Link = proposal.Link;
        ActionKind = proposal.Action?.Kind;
        ActionPayload = proposal.Action?.Payload;
        RuleVersion = ruleVersion;
        SeenAt = now;
    }

    /// <summary>Une règle est du code : une proposition mal formée est une erreur de programmation, pas une saisie.</summary>
    private static void Validate(DecisionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        Require(proposal.Kind, MaxKindLength, nameof(proposal.Kind));
        Require(proposal.Subject, MaxSubjectLength, nameof(proposal.Subject));
        Require(proposal.Link, MaxLinkLength, nameof(proposal.Link));
        ArgumentNullException.ThrowIfNull(proposal.Facts);
        if (proposal.Facts.Count > MaxFacts || proposal.Facts.Any(f => f.Key.Length is 0 or > MaxFactKeyLength || f.Value is null || f.Value.Length > MaxFactValueLength))
        {
            throw new ArgumentException("Faits trop nombreux ou trop longs.", nameof(proposal));
        }

        if (proposal.Action is { } action)
        {
            Require(action.Kind, MaxKindLength, nameof(proposal.Action));
            Require(action.Payload, MaxPayloadLength, nameof(proposal.Action));
        }

        if (!Enum.IsDefined(proposal.Audience) || !Enum.IsDefined(proposal.Urgency))
        {
            throw new ArgumentException("Public ou urgence inconnus.", nameof(proposal));
        }
    }

    private static void Require(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new ArgumentException($"{name} vide ou trop long.", name);
        }
    }
}

/// <summary>Le cadran d'autonomie du dossier : jusqu'où Lettra va seul pour une décision.</summary>
public enum AutonomyLevel
{
    /// <summary>Montrer : la décision s'affiche, l'utilisateur fait l'action lui-même depuis l'écran du détail.</summary>
    Show = 0,

    /// <summary>Préparer (par défaut) : l'action est préparée, un clic sur « Accepter » la fait avec les droits de l'utilisateur.</summary>
    Prepare = 1,
}

/// <summary>Les réglages des décisions du dossier : le cadran d'autonomie et la dernière actualisation. Une ligne par dossier.</summary>
public sealed class DecisionPolicy : Entity<Guid>, IVersioned
{
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-646563697369");

    private DecisionPolicy(Guid id)
        : base(id)
    {
    }

    public AutonomyLevel Level { get; private set; }

    /// <summary>La dernière actualisation des décisions (passe quotidienne ou « Actualiser »).</summary>
    public DateTimeOffset? RefreshedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public long Version { get; private set; }

    public static DecisionPolicy Default() => new(SingletonId) { Level = AutonomyLevel.Prepare };

    public Result SetLevel(AutonomyLevel level, Guid user, DateTimeOffset now)
    {
        if (!Enum.IsDefined(level))
        {
            return DecisionErrors.LevelInvalid;
        }

        Level = level;
        UpdatedAt = now;
        UpdatedBy = user;
        return Result.Success();
    }

    public void MarkRefreshed(DateTimeOffset now) => RefreshedAt = now;
}

public static class DecisionErrors
{
    public const string NotFoundCode = "Decision.NotFound";
    public const string NotOpenCode = "Decision.NotOpen";
    public const string NotRestorableCode = "Decision.NotRestorable";
    public const string SnoozeDateInvalidCode = "Decision.SnoozeDateInvalid";
    public const string NoActionCode = "Decision.NoAction";
    public const string ActionUnavailableCode = "Decision.ActionUnavailable";
    public const string LevelInvalidCode = "Decision.LevelInvalid";
    public const string ChoiceInvalidCode = "Decision.ChoiceInvalid";

    public static Error NotFound => Error.NotFound(NotFoundCode);

    /// <summary>Elle a déjà été acceptée, ignorée ou réglée (par quelqu'un d'autre, ou d'elle-même).</summary>
    public static Error NotOpen => Error.Conflict(NotOpenCode);

    public static Error NotRestorable => Error.Conflict(NotRestorableCode);

    public static Error SnoozeDateInvalid => Error.Validation(SnoozeDateInvalidCode, "SnoozeUntil");

    /// <summary>Rien à accepter : la décision n'a pas d'action préparée, ou le cadran est sur « Montrer ».</summary>
    public static Error NoAction => Error.Conflict(NoActionCode);

    /// <summary>L'action préparée n'a plus de gestionnaire sur cette installation (module retiré).</summary>
    public static Error ActionUnavailable => Error.Conflict(ActionUnavailableCode);

    public static Error LevelInvalid => Error.Validation(LevelInvalidCode, "Level");

    public static Error ChoiceInvalid => Error.Validation(ChoiceInvalidCode, "Choice");
}
