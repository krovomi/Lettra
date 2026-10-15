using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Billing;

/// <summary>
/// Régime de TVA d'un document, choisi à la saisie. Il fixe la catégorie de TVA des lignes et la mention
/// légale imprimée (référentiel Paperasse, mentions spéciales).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatRegimeDto>))]
public enum VatRegimeDto
{
    /// <summary>TVA française aux taux de chaque ligne (une ligne peut être exonérée avec un motif).</summary>
    Standard = 1,

    /// <summary>Franchise en base : « TVA non applicable, article 293 B du CGI ».</summary>
    Franchise = 2,

    /// <summary>Prestation de services intra-UE à un assujetti : autoliquidation (art. 283-2 CGI).</summary>
    ReverseChargeIntraEu = 3,

    /// <summary>Sous-traitance du BTP : autoliquidation (art. 283-2 nonies CGI).</summary>
    ReverseChargeConstruction = 4,

    /// <summary>Livraison intracommunautaire de biens : exonération (art. 262 ter I CGI).</summary>
    IntraEuGoodsSupply = 5,

    /// <summary>Exportation hors UE : exonération (art. 262 I CGI).</summary>
    Export = 6,
}

/// <summary>Catégorie de TVA d'une ligne (codes EN 16931 / UNTDID 5305, repris par Factur-X en phase 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatCategoryDto>))]
public enum VatCategoryDto
{
    /// <summary>S : taux normal, intermédiaire ou réduit.</summary>
    Standard = 1,

    /// <summary>Z : taux zéro.</summary>
    ZeroRated = 2,

    /// <summary>E : exonérée, avec un motif.</summary>
    Exempt = 3,

    /// <summary>AE : autoliquidation par le preneur.</summary>
    ReverseCharge = 4,

    /// <summary>K : livraison intracommunautaire.</summary>
    IntraCommunity = 5,

    /// <summary>G : exportation hors UE.</summary>
    Export = 6,

    /// <summary>O : hors champ (franchise en base).</summary>
    OutOfScope = 7,
}

/// <summary>Catégorie d'opération, mention obligatoire de la réforme 2026, déduite des lignes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationCategoryDto>))]
public enum OperationCategoryDto
{
    Goods = 1,
    Services = 2,
    Mixed = 3,
}

/// <summary>
/// Ligne saisie d'un devis ou d'une facture. Une ligne venue du catalogue en copie les valeurs : la modifier
/// ne touche pas l'article, et modifier l'article ne touche pas la ligne.
/// </summary>
public sealed record DocumentLineRequest
{
    /// <summary>Article d'origine, s'il y en a un (sert aux tarifs par client et aux statistiques).</summary>
    public Guid? CatalogItemId { get; set; }

    [StringLength(50, ErrorMessage = ErrorCodes.Billing.LineReferenceTooLong)]
    public string? Reference { get; set; }

    [Required(ErrorMessage = ErrorCodes.Billing.LineDesignationRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.Billing.LineDesignationTooLong)]
    public string Designation { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = ErrorCodes.Billing.LineDescriptionTooLong)]
    public string? Description { get; set; }

    public CatalogItemKindDto Kind { get; set; } = CatalogItemKindDto.Service;

    /// <summary>Code d'unité CEE-ONU (rec. 20).</summary>
    [Required(ErrorMessage = ErrorCodes.CatalogItem.UnitUnknown)]
    public string UnitCode { get; set; } = "C62";

    public decimal Quantity { get; set; } = 1m;

    /// <summary>Prix unitaire HT en euros (4 décimales retenues).</summary>
    public decimal UnitPriceExclTax { get; set; }

    /// <summary>Remise de ligne en pourcentage (0 à 100).</summary>
    [Range(0d, 100d, ErrorMessage = ErrorCodes.Billing.LineDiscountInvalid)]
    public decimal DiscountPercent { get; set; }

    /// <summary>Code du taux (<c>FR-20</c>…). Ignoré, et ramené à <c>FR-0</c>, hors régime <see cref="VatRegimeDto.Standard"/>.</summary>
    public string VatRateCode { get; set; } = "FR-20";

    /// <summary>Motif d'exonération (<see cref="BillingReferenceData.ExemptionReasons"/>) ; la ligne passe alors à 0 %.</summary>
    public string? VatExemptionCode { get; set; }
}

/// <summary>Ligne d'un document telle qu'affichée, montant HT calculé par le serveur.</summary>
public sealed record DocumentLineResponse(
    int Position,
    Guid? CatalogItemId,
    string? Reference,
    string Designation,
    string? Description,
    CatalogItemKindDto Kind,
    string UnitCode,
    decimal Quantity,
    decimal UnitPriceExclTax,
    decimal DiscountPercent,
    string VatRateCode,
    decimal VatRatePercent,
    VatCategoryDto VatCategory,
    string? VatExemptionCode,
    decimal TotalExclTax);

/// <summary>Ventilation par catégorie et taux : base HT, TVA, et la mention légale de la catégorie s'il en faut une.</summary>
public sealed record VatBreakdownResponse(
    VatCategoryDto VatCategory,
    decimal VatRatePercent,
    decimal BaseExclTax,
    decimal VatAmount,
    string? ExemptionCode);

/// <summary>Totaux qui font foi, calculés par <c>Lettra.Calculations.DocumentCalculator</c> (règle d'arrondi unique).</summary>
public sealed record DocumentTotalsResponse(
    IReadOnlyList<VatBreakdownResponse> Breakdown,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax);

/// <summary>Copie d'un tiers (émetteur ou client) figée sur le document à sa validation.</summary>
public sealed record PartySnapshotResponse(
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string PostalCode,
    string City,
    string CountryCode,
    string? Siren,
    string? Siret,
    string? VatNumber,
    string? LegalForm,
    decimal? ShareCapital,
    string? Rcs);

/// <summary>Gravité d'un constat de conformité.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplianceSeverityDto>))]
public enum ComplianceSeverityDto
{
    /// <summary>Bloque la validation.</summary>
    Error = 1,

    /// <summary>À corriger de préférence, ne bloque pas.</summary>
    Warning = 2,

    /// <summary>Mention présente.</summary>
    Ok = 3,
}

/// <summary>
/// Un constat de la checklist des mentions obligatoires. <paramref name="Code"/> est un code
/// <c>Mention.*</c> de <see cref="ErrorCodes.Mention"/>, traduit par l'UI ; <paramref name="Line"/> désigne la ligne concernée.
/// </summary>
public sealed record ComplianceFindingResponse(string Code, ComplianceSeverityDto Severity, string? LegalBasis, int? Line = null);

/// <summary>Checklist de conformité d'un document (le serveur la recalcule à la validation, et elle seule fait foi).</summary>
public sealed record ComplianceReportResponse(bool BlocksValidation, IReadOnlyList<ComplianceFindingResponse> Findings);

/// <summary>Motif d'exonération proposé à la saisie, avec sa mention légale (en français : c'est le texte imprimé).</summary>
public sealed record VatExemptionReasonOption(string Code, string LegalText);

/// <summary>Régime de TVA proposé, avec la mention légale qu'il imprime (vide pour le régime standard).</summary>
public sealed record VatRegimeOption(VatRegimeDto Regime, VatCategoryDto LineCategory, string? LegalText);

/// <summary>Listes fermées de la facturation, servies par le serveur.</summary>
public sealed record BillingReferenceData(
    IReadOnlyList<VatRegimeOption> VatRegimes,
    IReadOnlyList<VatExemptionReasonOption> ExemptionReasons,
    IReadOnlyList<VatRateOption> VatRates);
