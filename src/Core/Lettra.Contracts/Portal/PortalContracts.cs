using System.ComponentModel.DataAnnotations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Portal;

/// <summary>
/// Créer un lien d'espace client pour un client du dossier. Avec <see cref="QuoteId"/>, c'est un lien de signature de ce
/// devis, qui peut demander un acompte (<see cref="DownPaymentPercent"/>) à la signature.
/// </summary>
public sealed record CreatePortalLinkRequest
{
    public Guid CustomerId { get; set; }

    public Guid? QuoteId { get; set; }

    [Range(0.01d, 100d, ErrorMessage = ErrorCodes.Portal.DownPaymentInvalid)]
    public decimal? DownPaymentPercent { get; set; }

    [Range(1, 90, ErrorMessage = ErrorCodes.Portal.ValidityInvalid)]
    public int ValidityDays { get; set; } = 30;
}

/// <summary>
/// Le lien créé. Le jeton n'est rendu qu'ici, une seule fois : Lettra n'en garde que l'empreinte. L'adresse à envoyer au
/// client est <c>{adresse du serveur}/espace-client/{Token}</c>.
/// </summary>
public sealed record PortalLinkResponse(Guid Id, string Token, string Path, DateTimeOffset ExpiresAt, Guid? QuoteId, decimal? DownPaymentPercent);

/// <summary>Un lien d'espace client d'un client, sans son jeton.</summary>
public sealed record PortalLinkSummary(
    Guid Id,
    Guid? QuoteId,
    decimal? DownPaymentPercent,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastOpenedAt,
    DateTimeOffset? RevokedAt,
    bool IsActive);

/// <summary>La preuve de signature d'un devis signé en ligne.</summary>
public sealed record QuoteSignatureResponse(
    Guid QuoteId,
    string SignerName,
    DateTimeOffset SignedAt,
    string DocumentHash,
    string Provider,
    string ProviderReference,
    Guid? DownPaymentInvoiceId);

/// <summary>Ce que voit le client dans son espace : l'entreprise, ses factures, ses devis.</summary>
public sealed record PortalView(
    string CompanyName,
    string? CompanyCity,
    string CustomerName,
    string? Iban,
    string? Bic,
    string? Language,
    IReadOnlyList<PortalInvoice> Invoices,
    IReadOnlyList<PortalQuote> Quotes,
    Guid? FocusQuoteId,
    decimal? DownPaymentPercent,
    bool CanPayOnline,
    DateTimeOffset LinkExpiresAt);

/// <summary>Une facture (ou un acompte) validée du client, avec ce qu'il reste à payer.</summary>
public sealed record PortalInvoice(
    Guid Id,
    string Number,
    InvoiceKindDto Kind,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal TotalInclTax,
    decimal Outstanding,
    bool IsOverdue);

/// <summary>Une ligne de devis, telle que le client la lit.</summary>
public sealed record PortalQuoteLine(string Designation, decimal Quantity, string? Unit, decimal UnitPriceExclTax, decimal TotalExclTax);

/// <summary>Un devis validé du client : à signer tant qu'il est valable, ou déjà signé.</summary>
public sealed record PortalQuote(
    Guid Id,
    string Number,
    DateOnly IssueDate,
    DateOnly? ValidUntil,
    string? Subject,
    QuoteStatusDto Status,
    bool IsExpired,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    bool CanSign,
    IReadOnlyList<PortalQuoteLine> Lines,
    QuoteSignatureResponse? Signature);

/// <summary>Signature d'un devis par le client : son nom et son accord explicite.</summary>
public sealed record SignQuoteRequest(string SignerName, bool Consent);

/// <summary>Le devis signé, et la facture d'acompte créée à la suite s'il en demandait un.</summary>
public sealed record SignQuoteResult(string QuoteNumber, QuoteSignatureResponse Signature, Guid? DownPaymentInvoiceId);

/// <summary>Paiement en ligne commencé : la page de paiement du prestataire où envoyer le client.</summary>
public sealed record OnlineCheckoutResponse(string CheckoutId, string RedirectUrl, decimal Amount);

/// <summary>Issue d'un paiement en ligne : encaissement enregistré, ou refus du prestataire.</summary>
public sealed record OnlinePaymentResult(bool Paid, string InvoiceNumber, decimal Amount, string? ProviderReference, string? PaymentNumber);
