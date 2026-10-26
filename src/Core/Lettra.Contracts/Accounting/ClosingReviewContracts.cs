using System.Text.Json.Serialization;

namespace Lettra.Contracts.Accounting;

/// <summary>Gravité d'un contrôle de révision : rien à signaler, à regarder, ou à traiter avant la clôture.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewSeverityDto>))]
public enum ReviewSeverityDto
{
    Ok = 1,
    Info = 2,
    Warning = 3,
}

/// <summary>Ce vers quoi mène une anomalie : le compte d'un client, une facture, ou le rapprochement bancaire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewItemKindDto>))]
public enum ReviewItemKindDto
{
    Customer = 1,
    Invoice = 2,
    Bank = 3,
}

/// <summary>Une anomalie d'un contrôle, avec la pièce ou le compte qui l'explique.</summary>
public sealed record ReviewItemResponse(ReviewItemKindDto Kind, Guid? Id, string Label, decimal? Amount, DateOnly? Date, int? Days = null);

/// <summary>
/// Un contrôle de révision : son code (texte traduit par l'écran), sa gravité, le nombre et le montant des anomalies,
/// une valeur mesurée éventuelle (taux apparent de TVA) et les anomalies elles-mêmes, les plus lourdes en tête.
/// </summary>
public sealed record ReviewCheckResponse(
    string Code,
    ReviewSeverityDto Severity,
    int Count,
    decimal? Amount,
    decimal? Measure,
    IReadOnlyList<ReviewItemResponse> Items);

/// <summary>Révision assistée d'un exercice à sa date de clôture : les contrôles, les plus graves en tête.</summary>
public sealed record ClosingReviewResponse(DateOnly FiscalYearStart, DateOnly ClosingDate, IReadOnlyList<ReviewCheckResponse> Checks);
