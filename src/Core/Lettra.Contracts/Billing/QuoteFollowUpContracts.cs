using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Billing;

/// <summary>Comment une relance de devis est partie.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteFollowUpChannelDto>))]
public enum QuoteFollowUpChannelDto
{
    /// <summary>Envoyée par Lettra à l'adresse e-mail du client.</summary>
    Email = 1,

    /// <summary>Faite par l'utilisateur (téléphone, rendez-vous, sa messagerie), puis notée dans Lettra.</summary>
    Manual = 2,
}

/// <summary>Ce qu'il est advenu d'une relance de devis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteFollowUpOutcomeDto>))]
public enum QuoteFollowUpOutcomeDto
{
    Sent = 1,

    /// <summary>L'envoi par e-mail a échoué : gardé dans l'historique, il ne compte pas comme une relance faite.</summary>
    Failed = 2,
}

/// <summary>Tri de l'historique des relances de devis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteFollowUpSortDto>))]
public enum QuoteFollowUpSortDto
{
    Date = 1,
    Quote = 2,
    Customer = 3,
    Amount = 4,
}

/// <summary>
/// Réglage des devis à relancer. <paramref name="WindowDays"/> : un devis sans réponse dont la fin de validité tombe dans
/// ce nombre de jours est à relancer (5 par défaut). <paramref name="EmailConfigured"/> : un serveur d'envoi est branché sur
/// cette installation ; sinon les relances se notent à la main.
/// </summary>
public sealed record QuoteFollowUpSettingsResponse(int WindowDays, bool EmailConfigured, long Version);

/// <summary>Régler le nombre de jours avant la fin de validité à partir duquel un devis est à relancer.</summary>
public sealed record UpdateQuoteFollowUpSettingsRequest
{
    [Range(1, 30, ErrorMessage = ErrorCodes.QuoteFollowUps.WindowInvalid)]
    public int WindowDays { get; set; } = 5;
}

/// <summary>
/// Un devis envoyé, sans réponse, dont la fin de validité approche : le constat (avec sa raison, son effet chiffré et
/// son échéance) que l'écran montre et que « Décisions à prendre » pourra reprendre. <paramref name="Due"/> : à relancer
/// (pas encore relancé depuis qu'il approche de sa fin). <paramref name="Probability"/> et
/// <paramref name="WeightedExclTax"/> viennent du carnet de commandes pondéré.
/// </summary>
public sealed record QuoteToFollowUpResponse(
    Guid QuoteId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string Language,
    DateOnly IssueDate,
    DateOnly ValidUntil,
    int DaysLeft,
    int DaysWithoutAnswer,
    decimal TotalExclTax,
    decimal TotalInclTax,
    decimal Probability,
    decimal WeightedExclTax,
    bool Due,
    int FollowUpCount,
    DateTimeOffset? LastFollowUpAt,
    QuoteFollowUpChannelDto? LastChannel);

/// <summary>Les devis dont la fin de validité tombe dans la fenêtre réglée, ceux à relancer d'abord, et le réglage.</summary>
public sealed record QuoteFollowUpOverviewResponse(
    IReadOnlyList<QuoteToFollowUpResponse> Quotes,
    QuoteFollowUpSettingsResponse Settings,
    int DueCount,
    decimal DueExclTax);

/// <summary>La relance proposée pour un devis, à relire et à modifier avant l'envoi.</summary>
public sealed record QuoteFollowUpPreviewResponse(
    Guid QuoteId,
    string QuoteNumber,
    string CustomerName,
    string? Recipient,
    string Language,
    string Subject,
    string Body,
    int DaysLeft,
    bool EmailConfigured);

/// <summary>
/// Envoyer la relance d'un devis par e-mail, ou noter qu'elle a été faite autrement. Sans objet ni texte, Lettra prend
/// ceux qu'il propose ; sinon ceux relus par l'utilisateur.
/// </summary>
public sealed record SendQuoteFollowUpRequest
{
    public QuoteFollowUpChannelDto Channel { get; set; } = QuoteFollowUpChannelDto.Email;

    [StringLength(300, ErrorMessage = ErrorCodes.QuoteFollowUps.MessageInvalid)]
    public string? Subject { get; set; }

    [StringLength(8000, ErrorMessage = ErrorCodes.QuoteFollowUps.MessageInvalid)]
    public string? Body { get; set; }
}

/// <summary>Une relance de devis de l'historique, telle qu'elle est partie.</summary>
public sealed record QuoteFollowUpResponse(
    Guid Id,
    Guid QuoteId,
    string QuoteNumber,
    Guid CustomerId,
    string CustomerName,
    QuoteFollowUpChannelDto Channel,
    QuoteFollowUpOutcomeDto Outcome,
    string? ErrorCode,
    string? Recipient,
    string Language,
    string Subject,
    string Body,
    decimal AmountExclTax,
    DateOnly ValidUntil,
    DateTimeOffset OccurredAt);
