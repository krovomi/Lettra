using System.Text.Json.Serialization;

namespace Lettra.Contracts.Billing;

/// <summary>Tranche de montant HT d'un devis : moins de 1 000 €, 1 000 à 5 000 €, 5 000 à 20 000 €, 20 000 € et plus.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteAmountBandDto>))]
public enum QuoteAmountBandDto
{
    Small = 1,
    Medium = 2,
    Large = 3,
    VeryLarge = 4,
}

/// <summary>Ancienneté d'un devis sans réponse : 7 jours au plus, 8 à 15 jours, 16 à 30 jours, plus de 30 jours.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteAgeBandDto>))]
public enum QuoteAgeBandDto
{
    FirstWeek = 1,
    SecondWeek = 2,
    SameMonth = 3,
    Older = 4,
}

/// <summary>
/// Un taux retenu pour un critère : <paramref name="Accepted"/> devis acceptés sur <paramref name="Closed"/> conclus.
/// <paramref name="UsesGlobalRate"/> : trop peu de devis pour ce critère, le taux global est pris à la place.
/// </summary>
public sealed record CriterionRateDto(int Accepted, int Closed, decimal Rate, bool UsesGlobalRate);

/// <summary>Taux d'acceptation passé d'une tranche de montant ; <paramref name="Rate"/> absent sans devis conclu.</summary>
public sealed record AmountBandRateDto(QuoteAmountBandDto Band, int Accepted, int Closed, decimal? Rate);

/// <summary>Part acceptée des devis restés sans réponse au moins jusqu'au début de la tranche d'ancienneté.</summary>
public sealed record AgeBandRateDto(QuoteAgeBandDto Band, int Accepted, int Closed, decimal? Rate);

/// <summary>
/// Un devis en attente, pondéré : sa probabilité est la moyenne des taux du client, de sa tranche de montant et de son
/// ancienneté ; ses montants pondérés sont ses montants multipliés par cette probabilité (de 0 à 1).
/// </summary>
public sealed record OrderBookQuoteResponse(
    Guid QuoteId,
    string Number,
    Guid CustomerId,
    string CustomerName,
    DateOnly IssueDate,
    DateOnly ValidUntil,
    int AgeDays,
    decimal TotalExclTax,
    decimal TotalInclTax,
    QuoteAmountBandDto AmountBand,
    QuoteAgeBandDto AgeBand,
    CriterionRateDto Customer,
    CriterionRateDto Amount,
    CriterionRateDto Age,
    decimal Probability,
    decimal WeightedExclTax,
    decimal WeightedInclTax);

/// <summary>
/// Le carnet de commandes pondéré du jour : le chiffre d'affaires à venir des devis en attente, pondéré par les taux
/// d'acceptation des <paramref name="HistoryMonths"/> derniers mois, et ces taux. Calcul déterministe, sans IA ; un
/// critère qui repose sur moins de <paramref name="MinimumSample"/> devis prend le taux global, et le taux global vaut
/// une chance sur deux tant que l'historique est insuffisant (<paramref name="HasEnoughHistory"/> faux). Les montants
/// TTC servent à la prévision de trésorerie. <paramref name="ExcludedCount"/> et <paramref name="ExcludedExclTax"/> :
/// les devis en attente des clients opposés au profilage, hors calcul (ni taux, ni pondération).
/// </summary>
public sealed record OrderBookResponse(
    DateOnly AsOf,
    DateOnly HistoryFrom,
    int HistoryMonths,
    int MinimumSample,
    int Accepted,
    int Closed,
    decimal GlobalRate,
    bool HasEnoughHistory,
    decimal OpenExclTax,
    decimal WeightedExclTax,
    decimal WeightedInclTax,
    IReadOnlyList<AmountBandRateDto> ByAmount,
    IReadOnlyList<AgeBandRateDto> ByAge,
    IReadOnlyList<OrderBookQuoteResponse> Quotes,
    int ExcludedCount = 0,
    decimal ExcludedExclTax = 0m);
