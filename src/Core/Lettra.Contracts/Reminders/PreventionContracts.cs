using System.Text.Json.Serialization;
using Lettra.Contracts.Insights;

namespace Lettra.Contracts.Reminders;

/// <summary>Où en est un échéancier : à jour, une échéance manquée, non tenu (dernière échéance passée), soldé, arrêté.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentPlanStatusDto>))]
public enum PaymentPlanStatusDto
{
    OnTrack = 1,
    Late = 2,
    Lapsed = 3,
    Completed = 4,
    Cancelled = 5,
}

/// <summary>
/// Un rappel avant échéance : une facture pas encore échue d'un client dont la météo des paiements annonce pluie ou
/// orage. <paramref name="PlannedOn"/> est le jour prévu (avancé au meilleur jour du client, <paramref name="BestDay"/>,
/// s'il en a un) ; <paramref name="DueToday"/> dit qu'il est à envoyer ; <paramref name="SentAt"/> qu'il est parti.
/// </summary>
public sealed record UpcomingReminderResponse(
    Guid InvoiceId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string Language,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal Outstanding,
    PaymentWeatherDto Weather,
    DayOfWeek? BestDay,
    DateOnly PlannedOn,
    bool DueToday,
    DateTimeOffset? SentAt);

/// <summary>Une échéance d'un échéancier et son suivi.</summary>
public sealed record PaymentPlanInstallmentResponse(int Number, DateOnly DueDate, decimal Amount, bool Paid, bool Missed, decimal Remaining);

/// <summary>
/// Un échéancier demandé par un client dans son espace client. <paramref name="Acknowledged"/> : l'entreprise l'a vu
/// (l'alerte du dossier attend tant qu'un échéancier ne l'est pas) ; <paramref name="Version"/> sert à le prendre en
/// compte ou à y mettre fin sans écraser un changement fait entre-temps.
/// </summary>
public sealed record PaymentPlanResponse(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    PaymentPlanStatusDto Status,
    decimal Total,
    decimal Outstanding,
    IReadOnlyList<PaymentPlanInstallmentResponse> Installments,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    bool Acknowledged,
    DateTimeOffset? CancelledAt,
    long Version);

/// <summary>L'échéancier d'une facture (le dernier demandé), ou rien.</summary>
public sealed record InvoicePaymentPlanResponse(PaymentPlanResponse? Plan);

/// <summary>Prendre en compte un échéancier, ou y mettre fin, à la version lue.</summary>
public sealed record PaymentPlanDecisionRequest(long Version);

/// <summary>Un client opposé au ciblage selon son comportement de paiement : le rappel avant échéance ne lui est plus proposé.</summary>
public sealed record TargetingObjectionResponse(Guid CustomerId, string CustomerName, DateTimeOffset RecordedAt);

/// <summary>Noter ou retirer l'opposition d'un client au ciblage.</summary>
public sealed record SetTargetingObjectionRequest(bool Objects);
