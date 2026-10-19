using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Billing;

/// <summary>Nature d'une pièce de la série des factures (FAC).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceKindDto>))]
public enum InvoiceKindDto
{
    /// <summary>Facture (y compris la facture finale d'un devis, qui déduit les acomptes).</summary>
    Invoice = 1,

    /// <summary>Facture d'acompte.</summary>
    DownPayment = 2,

    /// <summary>Avoir : annule tout ou partie d'une facture validée. Montants positifs, signe porté par la nature.</summary>
    CreditNote = 3,
}

/// <summary>Cycle de vie d'une facture ou d'un avoir.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceStatusDto>))]
public enum InvoiceStatusDto
{
    /// <summary>Brouillon : modifiable, supprimable, sans numéro.</summary>
    Draft = 1,

    /// <summary>Numéroté (série FAC), figé, inscrit au journal : ne se corrige plus que par un avoir.</summary>
    Validated = 2,

    /// <summary>Émis : envoyé au client (ou à la plateforme agréée en phase 5).</summary>
    Issued = 3,
}

/// <summary>Création ou modification d'une facture brouillon (même forme pour les deux).</summary>
public sealed record SaveInvoiceRequest
{
    [Required(ErrorMessage = ErrorCodes.Billing.CustomerRequired)]
    public Guid CustomerId { get; set; }

    /// <summary>Date de livraison ou d'exécution, si elle diffère de la date d'émission.</summary>
    public DateOnly? DeliveryDate { get; set; }

    /// <summary>Échéance ; à défaut, calculée à la validation depuis le délai de la fiche entreprise.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Conditions de paiement imprimées ; à défaut, celles de la fiche entreprise.</summary>
    [StringLength(500, ErrorMessage = ErrorCodes.Billing.PaymentTermsTooLong)]
    public string? PaymentTerms { get; set; }

    /// <summary>Adresse de livraison si elle diffère de l'adresse de facturation (mention 2026).</summary>
    [StringLength(500, ErrorMessage = ErrorCodes.Billing.DeliveryAddressTooLong)]
    public string? DeliveryAddress { get; set; }

    public VatRegimeDto VatRegime { get; set; } = VatRegimeDto.Standard;

    /// <summary>Référence du bon de commande du client (EN 16931 BT-13).</summary>
    [StringLength(100, ErrorMessage = ErrorCodes.Billing.PurchaseOrderReferenceTooLong)]
    public string? PurchaseOrderReference { get; set; }

    [StringLength(200, ErrorMessage = ErrorCodes.Billing.SubjectTooLong)]
    public string? Subject { get; set; }

    [StringLength(4000, ErrorMessage = ErrorCodes.Billing.NotesTooLong)]
    public string? Notes { get; set; }

    public List<DocumentLineRequest> Lines { get; set; } = [];
}

/// <summary>
/// Facture, acompte ou avoir tel qu'affiché. <paramref name="Version"/> sert à la concurrence optimiste ;
/// <paramref name="ContentHash"/> est l'empreinte SHA-256 de la forme canonique (<see cref="CanonicalDocument"/>),
/// figée à la validation et inscrite au journal inaltérable. <paramref name="CreditApprovedBy"/> : manager qui a
/// approuvé l'avoir quand la séparation des tâches du cabinet l'exige. <paramref name="BillingPeriodStart"/> et
/// <paramref name="BillingPeriodEnd"/> : période facturée (BT-73/74) d'une facture récurrente.
/// </summary>
public sealed record InvoiceResponse(
    Guid Id,
    long Version,
    InvoiceKindDto Kind,
    InvoiceStatusDto Status,
    string? Number,
    string SeriesCode,
    Guid CustomerId,
    PartySnapshotResponse Customer,
    PartySnapshotResponse? Seller,
    DateOnly? IssueDate,
    DateOnly? DeliveryDate,
    DateOnly? DueDate,
    string? PaymentTerms,
    string? DeliveryAddress,
    VatRegimeDto VatRegime,
    OperationCategoryDto OperationCategory,
    string? PurchaseOrderReference,
    string? Subject,
    string? Notes,
    Guid? QuoteId,
    string? QuoteNumber,
    Guid? CreditedInvoiceId,
    string? CreditedInvoiceNumber,
    string? CreditReason,
    IReadOnlyList<DocumentLineResponse> Lines,
    DocumentTotalsResponse Totals,
    IReadOnlyList<string> LegalMentions,
    decimal CreditedInclTax,
    string? ContentHash,
    Guid CreatedBy,
    Guid? ValidatedBy,
    Guid? IssuedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? IssuedAt,
    Guid? CreditApprovedBy = null,
    DateTimeOffset? CreditApprovedAt = null,
    decimal PaidInclTax = 0m,
    DateOnly? BillingPeriodStart = null,
    DateOnly? BillingPeriodEnd = null);

/// <summary>Ligne de la liste des factures et avoirs.</summary>
public sealed record InvoiceSummary(
    Guid Id,
    InvoiceKindDto Kind,
    InvoiceStatusDto Status,
    string? Number,
    Guid CustomerId,
    string CustomerName,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal TotalExclTax,
    decimal TotalInclTax);

/// <summary>Une ligne de la facture d'origine à créditer, en tout ou partie.</summary>
public sealed record CreditNoteLineRequest(int Position, decimal Quantity);

/// <summary>
/// Avoir sur une facture validée. Sans lignes, l'avoir reprend toute la facture (refusé si elle est déjà en partie créditée) ;
/// avec des lignes, il crédite les quantités données (rang de ligne, quantité positive), dans la limite du solde.
/// L'avoir est créé en brouillon ; sa validation le numérote dans la série des factures.
/// </summary>
public sealed record CreateCreditNoteRequest
{
    [StringLength(500, ErrorMessage = ErrorCodes.Billing.CreditReasonTooLong)]
    public string? Reason { get; set; }

    public List<CreditNoteLineRequest>? Lines { get; set; }
}
