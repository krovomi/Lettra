using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Catalog;

/// <summary>Nature de l'article : livraison de biens ou prestation de services (mention obligatoire des factures).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatalogItemKindDto>))]
public enum CatalogItemKindDto
{
    Good = 1,
    Service = 2,
}

/// <summary>
/// Création ou modification d'un article (même forme pour les deux). Le prix est saisi HT ; l'écran
/// peut afficher et accepter le TTC grâce à <c>Lettra.Calculations.PriceConversion</c>.
/// </summary>
public sealed record SaveCatalogItemRequest
{
    [Required(ErrorMessage = ErrorCodes.CatalogItem.ReferenceRequired)]
    [StringLength(50, ErrorMessage = ErrorCodes.CatalogItem.ReferenceTooLong)]
    public string Reference { get; set; } = string.Empty;

    [Required(ErrorMessage = ErrorCodes.CatalogItem.DesignationRequired)]
    [StringLength(200, ErrorMessage = ErrorCodes.CatalogItem.DesignationTooLong)]
    public string Designation { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = ErrorCodes.CatalogItem.DescriptionTooLong)]
    public string? Description { get; set; }

    public CatalogItemKindDto Kind { get; set; } = CatalogItemKindDto.Service;

    /// <summary>Code d'unité CEE-ONU (rec. 20) : <c>C62</c> unité, <c>HUR</c> heure, <c>LS</c> forfait…</summary>
    [Required(ErrorMessage = ErrorCodes.CatalogItem.UnitUnknown)]
    public string UnitCode { get; set; } = "C62";

    /// <summary>Prix unitaire hors taxes en euros (4 décimales retenues).</summary>
    [Range(0d, 999_999_999.9999d, ErrorMessage = ErrorCodes.CatalogItem.PriceNegative)]
    public decimal UnitPriceExclTax { get; set; }

    /// <summary>Code du taux de TVA (<c>FR-20</c>, <c>FR-10</c>, <c>FR-5.5</c>…).</summary>
    [Required(ErrorMessage = ErrorCodes.SharedKernel.VatRateUnknown)]
    public string VatRateCode { get; set; } = "FR-20";
}

/// <summary>Article tel qu'affiché.</summary>
public sealed record CatalogItemResponse(
    Guid Id,
    string Reference,
    string Designation,
    string? Description,
    CatalogItemKindDto Kind,
    string UnitCode,
    decimal UnitPriceExclTax,
    string VatRateCode,
    decimal VatRatePercent,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Ligne de la liste des articles, et choix proposés par la liste F4 des devis et factures.</summary>
public sealed record CatalogItemSummary(
    Guid Id,
    string Reference,
    string Designation,
    CatalogItemKindDto Kind,
    string UnitCode,
    decimal UnitPriceExclTax,
    string VatRateCode,
    decimal VatRatePercent,
    bool IsArchived);

/// <summary>Archive (<c>true</c>) ou réactive (<c>false</c>) un article.</summary>
public sealed record SetCatalogItemArchivedRequest(bool IsArchived);

/// <summary>Taux de TVA proposé à la saisie.</summary>
public sealed record VatRateOption(string Code, decimal Percent);

/// <summary>Listes fermées du catalogue, servies par le serveur : le WASM n'en garde pas de copie.</summary>
public sealed record CatalogReferenceData(IReadOnlyList<string> UnitCodes, IReadOnlyList<VatRateOption> VatRates);
