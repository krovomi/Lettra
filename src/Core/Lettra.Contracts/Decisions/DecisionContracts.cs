using System.Text.Json.Serialization;

namespace Lettra.Contracts.Decisions;

/// <summary>Où en est une décision : à décider, plus tard, acceptée, ignorée, réglée d'elle-même.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DecisionStatusDto>))]
public enum DecisionStatusDto
{
    Proposed = 1,
    Snoozed = 2,
    Accepted = 3,
    Dismissed = 4,
    Resolved = 5,
}

/// <summary>Pour qui est la décision.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DecisionAudienceDto>))]
public enum DecisionAudienceDto
{
    Cabinet = 1,
    Entreprise = 2,
    ClientFinal = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter<DecisionUrgencyDto>))]
public enum DecisionUrgencyDto
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Le cadran d'autonomie du dossier : montrer seulement, ou préparer l'action (un clic l'accepte).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutonomyLevelDto>))]
public enum AutonomyLevelDto
{
    Show = 0,
    Prepare = 1,
}

/// <summary>La réponse de l'utilisateur à une décision.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DecisionChoiceDto>))]
public enum DecisionChoiceDto
{
    /// <summary>Faire l'action préparée, avec les droits de l'utilisateur.</summary>
    Accept = 1,

    /// <summary>Plus tard : jusqu'au jour donné.</summary>
    Snooze = 2,

    /// <summary>Ignorer : elle n'est plus proposée.</summary>
    Dismiss = 3,

    /// <summary>Remettre dans la liste une décision ignorée ou remise à plus tard.</summary>
    Restore = 4,
}

/// <summary>
/// Une décision à prendre. <paramref name="Kind"/> nomme ses phrases (<c>Decision.&lt;Kind&gt;.Title</c> et <c>.Reason</c>),
/// remplies par <paramref name="Facts"/> : une valeur qui commence par <c>@</c> est une clé de libellé, une date est écrite
/// <c>aaaa-mm-jj</c>, un fait dont le nom finit par <c>Amount</c> est un montant en euros. <paramref name="Link"/> est
/// l'écran du détail, relatif à la base. <paramref name="CanAccept"/> : une action est préparée et le cadran la permet.
/// </summary>
public sealed record DecisionRow(
    Guid Id,
    string Kind,
    DecisionAudienceDto Audience,
    DecisionUrgencyDto Urgency,
    decimal? Impact,
    DateOnly? DueOn,
    IReadOnlyDictionary<string, string> Facts,
    string Link,
    bool CanAccept,
    DecisionStatusDto Status,
    DateOnly? SnoozedUntil,
    DateTimeOffset ProposedAt,
    DateTimeOffset? ClosedAt,
    long Version);

/// <summary>
/// Les décisions du dossier que l'utilisateur peut traiter : ouvertes, et closes depuis trente jours. <paramref name="PolicyVersion"/>
/// est vide tant que le cadran n'a jamais été réglé ni actualisé ; <paramref name="CanConfigure"/> : l'utilisateur peut le régler.
/// </summary>
public sealed record DecisionsResponse(
    IReadOnlyList<DecisionRow> Rows,
    AutonomyLevelDto Level,
    long? PolicyVersion,
    DateTimeOffset? RefreshedAt,
    bool CanConfigure);

/// <summary>Répondre à une décision (concurrence optimiste par <c>If-Match</c>). <see cref="SnoozeUntil"/> sert à « Plus tard ».</summary>
public sealed record AnswerDecisionRequest
{
    public DecisionChoiceDto Choice { get; set; }

    public DateOnly? SnoozeUntil { get; set; }
}

/// <summary>Régler le cadran d'autonomie du dossier (concurrence optimiste par <c>If-Match</c>).</summary>
public sealed record SetDecisionPolicyRequest
{
    public AutonomyLevelDto Level { get; set; } = AutonomyLevelDto.Prepare;
}
