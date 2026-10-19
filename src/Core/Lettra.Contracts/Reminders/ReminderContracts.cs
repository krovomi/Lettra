using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Reminders;

/// <summary>Niveau du scénario de relance : rappel amical, relance, dernière relance, mise en demeure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderLevelDto>))]
public enum ReminderLevelDto
{
    Friendly = 1,
    Reminder = 2,
    Final = 3,
    FormalNotice = 4,
}

/// <summary>Envoyée par Lettra (e-mail) ou par le cabinet lui-même puis notée (courrier, téléphone…).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderChannelDto>))]
public enum ReminderChannelDto
{
    Email = 1,
    Manual = 2,
}

/// <summary>Partie, ou envoi par e-mail en échec (gardé dans l'historique, ne compte pas comme relance faite).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderOutcomeDto>))]
public enum ReminderOutcomeDto
{
    Sent = 1,
    Failed = 2,
}

/// <summary>Déclenchée par un utilisateur ou par la passe quotidienne (envoi automatique).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderTriggerDto>))]
public enum ReminderTriggerDto
{
    User = 1,
    Schedule = 2,
}

/// <summary>Tri de l'historique des relances.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReminderSortDto>))]
public enum ReminderSortDto
{
    Date = 1,
    Invoice = 2,
    Customer = 3,
    Level = 4,
}

/// <summary>
/// Scénario de relance du dossier. <paramref name="Delays"/> : jours de retard qui déclenchent chaque niveau, dans
/// l'ordre (J+3, J+15, J+30, J+45 par défaut). <paramref name="EmailConfigured"/> : un serveur d'envoi est branché sur
/// cette installation ; sinon les relances se notent à la main.
/// </summary>
public sealed record ReminderSettingsResponse(bool AutoSend, IReadOnlyList<int> Delays, int MinimumGapDays, bool EmailConfigured, long Version);

public sealed record UpdateReminderSettingsRequest
{
    public bool AutoSend { get; set; }

    [Required(ErrorMessage = ErrorCodes.Reminders.DelaysInvalid)]
    public IReadOnlyList<int> Delays { get; set; } = [];

    [Range(1, 90, ErrorMessage = ErrorCodes.Reminders.GapInvalid)]
    public int MinimumGapDays { get; set; } = 7;
}

/// <summary>
/// Une facture échue et non soldée, vue par les relances. <paramref name="NextLevel"/> est le niveau suivant (nul après la
/// mise en demeure) ; <paramref name="DueToday"/> dit si le scénario l'envoie aujourd'hui. Les pénalités sont
/// proposées, jamais facturées d'office.
/// </summary>
public sealed record ReminderInvoiceResponse(
    Guid InvoiceId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string Language,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal Outstanding,
    int DaysOverdue,
    ReminderLevelDto? LastLevel,
    DateTimeOffset? LastSentAt,
    ReminderLevelDto? NextLevel,
    bool DueToday,
    decimal PenaltyInterest,
    decimal RecoveryIndemnity);

/// <summary>L'écran Relances : les factures échues, le scénario, et le nombre de relances dues aujourd'hui.</summary>
public sealed record ReminderOverviewResponse(IReadOnlyList<ReminderInvoiceResponse> Invoices, ReminderSettingsResponse Settings, int DueToday);

/// <summary>Le texte de la prochaine relance d'une facture, dans la langue du client, à relire avant l'envoi.</summary>
public sealed record ReminderPreviewResponse(Guid InvoiceId, ReminderLevelDto Level, string Language, string? Recipient, string Subject, string Body);

/// <summary>Envoyer la prochaine relance par e-mail, ou noter qu'elle est partie autrement.</summary>
public sealed record SendReminderRequest(ReminderChannelDto Channel);

/// <summary>Une relance de l'historique, telle qu'elle est partie.</summary>
public sealed record PaymentReminderResponse(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    ReminderLevelDto Level,
    ReminderChannelDto Channel,
    ReminderOutcomeDto Outcome,
    string? ErrorCode,
    string? Recipient,
    string Language,
    string Subject,
    string Body,
    decimal Outstanding,
    int DaysOverdue,
    ReminderTriggerDto Trigger,
    DateTimeOffset OccurredAt);

/// <summary>Bilan d'une passe de relances (bouton « Envoyer les relances du jour » ou passe quotidienne).</summary>
public sealed record RunRemindersResponse(int Due, int Sent, int Failed, int WithoutEmail);
