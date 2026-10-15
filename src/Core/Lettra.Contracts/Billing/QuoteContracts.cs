using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Billing;

/// <summary>Cycle de vie d'un devis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteStatusDto>))]
public enum QuoteStatusDto
{
    /// <summary>Brouillon, modifiable, sans numéro.</summary>
    Draft = 1,

    /// <summary>Numéroté (série DEV) et figé : prêt à être envoyé au client.</summary>
    Validated = 2,

    Accepted = 3,
    Refused = 4,

    /// <summary>La facture finale a été créée depuis le devis.</summary>
    Invoiced = 5,
}

/// <summary>Création ou modification d'un devis brouillon (même forme pour les deux).</summary>
public sealed record SaveQuoteRequest
{
    [Required(ErrorMessage = ErrorCodes.Billing.CustomerRequired)]
    public Guid CustomerId { get; set; }

    /// <summary>Fin de validité de l'offre ; par défaut 30 jours après la validation.</summary>
    public DateOnly? ValidUntil { get; set; }

    public VatRegimeDto VatRegime { get; set; } = VatRegimeDto.Standard;

    [StringLength(200, ErrorMessage = ErrorCodes.Billing.SubjectTooLong)]
    public string? Subject { get; set; }

    [StringLength(4000, ErrorMessage = ErrorCodes.Billing.NotesTooLong)]
    public string? Notes { get; set; }

    public List<DocumentLineRequest> Lines { get; set; } = [];
}

/// <summary>Devis tel qu'affiché. <paramref name="Version"/> sert à la concurrence optimiste (<c>ETag</c>).</summary>
public sealed record QuoteResponse(
    Guid Id,
    long Version,
    string? Number,
    QuoteStatusDto Status,
    Guid CustomerId,
    PartySnapshotResponse Customer,
    DateOnly? IssueDate,
    DateOnly? ValidUntil,
    bool IsExpired,
    VatRegimeDto VatRegime,
    string? Subject,
    string? Notes,
    IReadOnlyList<DocumentLineResponse> Lines,
    DocumentTotalsResponse Totals,
    IReadOnlyList<string> LegalMentions,
    decimal DownPaymentsInclTax,
    Guid? FinalInvoiceId,
    Guid CreatedBy,
    Guid? ValidatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ValidatedAt);

/// <summary>Ligne de la liste des devis.</summary>
public sealed record QuoteSummary(
    Guid Id,
    string? Number,
    QuoteStatusDto Status,
    Guid CustomerId,
    string CustomerName,
    DateOnly? IssueDate,
    DateOnly? ValidUntil,
    bool IsExpired,
    decimal TotalExclTax,
    decimal TotalInclTax);

/// <summary>Réponse du client sur un devis validé.</summary>
public sealed record SetQuoteOutcomeRequest(bool Accepted);

/// <summary>
/// Facture d'acompte sur un devis accepté : un pourcentage du TTC, ou un montant TTC. La TVA de l'acompte est
/// ventilée au prorata des taux du devis.
/// </summary>
public sealed record CreateDownPaymentRequest
{
    [Range(0.01d, 100d, ErrorMessage = ErrorCodes.Billing.DownPaymentInvalid)]
    public decimal? Percent { get; set; }

    [Range(0.01d, 999_999_999.99d, ErrorMessage = ErrorCodes.Billing.DownPaymentInvalid)]
    public decimal? AmountInclTax { get; set; }

    public DateOnly? DueDate { get; set; }
}
