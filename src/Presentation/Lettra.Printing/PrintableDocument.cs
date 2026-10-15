using System.Globalization;

namespace Lettra.Printing;

/// <summary>Nature du document imprimé : fixe le titre, les dates affichées et les mentions propres à chaque type.</summary>
public enum PrintableDocumentKind
{
    Quote,
    Invoice,
    DownPaymentInvoice,
    CreditNote,
}

/// <summary>
/// Partie (émetteur ou destinataire) telle qu'elle s'imprime. Les champs absents ne sont pas imprimés ;
/// c'est la checklist de conformité, pas le rendu, qui dit s'il manque une mention obligatoire.
/// </summary>
public sealed record PrintableParty
{
    public required string Name { get; init; }

    public IReadOnlyList<string> AddressLines { get; init; } = [];

    public string? Siren { get; init; }

    public string? VatNumber { get; init; }

    /// <summary>Forme juridique (« SAS »), pour le pied de page de l'émetteur.</summary>
    public string? LegalForm { get; init; }

    /// <summary>Capital social déjà formaté (« 10 000 € »).</summary>
    public string? ShareCapital { get; init; }

    /// <summary>Ville du greffe (« RCS Lyon ») ou mention RM pour un artisan.</summary>
    public string? Registration { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }
}

/// <summary>Ligne imprimée. Le montant HT n'est pas fourni : il est recalculé par la règle d'arrondi unique.</summary>
public sealed record PrintableLine(
    string? Reference,
    string Designation,
    decimal Quantity,
    string UnitLabel,
    decimal UnitPriceExclTax,
    decimal VatRatePercent,
    decimal DiscountPercent = 0m);

/// <summary>Acompte déjà facturé, déduit du net à payer d'une facture de solde.</summary>
public sealed record PrintableDeduction(string Number, DateOnly Date, decimal AmountInclTax);

/// <summary>
/// Tout ce qu'il faut pour imprimer un devis, une facture, une facture d'acompte ou un avoir. Modèle d'impression
/// propre à ce projet : l'UI le remplit depuis les contrats ; il ne traverse jamais la frontière client ↔ serveur.
/// </summary>
public sealed record PrintableDocument
{
    public required PrintableDocumentKind Kind { get; init; }

    /// <summary>Numéro attribué à la validation ; <c>null</c> pour un brouillon (imprimé avec un filigrane).</summary>
    public string? Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    /// <summary>Date de la vente ou de la prestation, si elle diffère de la date d'émission.</summary>
    public DateOnly? DeliveryDate { get; init; }

    /// <summary>Échéance de paiement (facture, acompte) ou fin de validité (devis).</summary>
    public DateOnly? DueDate { get; init; }

    public required PrintableParty Seller { get; init; }

    public required PrintableParty Buyer { get; init; }

    /// <summary>Adresse de livraison si elle diffère de l'adresse de facturation (mention de la réforme 2026).</summary>
    public IReadOnlyList<string> DeliveryAddressLines { get; init; } = [];

    public required IReadOnlyList<PrintableLine> Lines { get; init; }

    /// <summary>Document d'origine : facture d'un avoir, devis d'un acompte ou d'une facture.</summary>
    public string? RelatedDocumentNumber { get; init; }

    /// <summary>Acomptes déjà facturés, déduits du net à payer.</summary>
    public IReadOnlyList<PrintableDeduction> Deductions { get; init; } = [];

    /// <summary>Conditions de paiement, déjà rédigées dans la langue du document.</summary>
    public string? PaymentTerms { get; init; }

    /// <summary>
    /// Mentions légales du document (pénalités de retard, indemnité forfaitaire, exonération, autoliquidation…),
    /// déjà rédigées dans la langue du document par le référentiel des mentions.
    /// </summary>
    public IReadOnlyList<string> LegalMentions { get; init; } = [];

    public string? Iban { get; init; }

    public string? Bic { get; init; }

    public string? Notes { get; init; }

    /// <summary>Devise ISO 4217. Lettra facture en euros.</summary>
    public string Currency { get; init; } = "EUR";

    /// <summary>Langue du document : celle du client destinataire, pas celle de l'utilisateur (annexe A §4.3).</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.GetCultureInfo("fr-FR");
}
