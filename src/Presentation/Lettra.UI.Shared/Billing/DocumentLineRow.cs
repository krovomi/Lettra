using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Ligne de la grille d'un devis ou d'une facture. <see cref="Key"/> reste stable pendant toute la vie de la ligne
/// (la grille garde ainsi le focus). Le taux affiché est le taux effectif : 0 % pour une ligne exonérée ou hors du
/// régime de TVA standard, comme le serveur le retiendra.
/// </summary>
public sealed record DocumentLineRow(
    Guid Key,
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
    string? VatExemptionCode)
{
    /// <summary>Ligne vierge, ajoutée en fin de grille.</summary>
    public static DocumentLineRow New(VatRateOption defaultRate)
    {
        ArgumentNullException.ThrowIfNull(defaultRate);
        return new(Guid.NewGuid(), null, null, string.Empty, null, CatalogItemKindDto.Service, "C62", 1m, 0m, 0m,
            defaultRate.Code, defaultRate.Percent, null);
    }

    public static DocumentLineRow From(DocumentLineResponse line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(Guid.NewGuid(), line.CatalogItemId, line.Reference, line.Designation, line.Description, line.Kind, line.UnitCode,
            line.Quantity, line.UnitPriceExclTax, line.DiscountPercent, line.VatRateCode, line.VatRatePercent, line.VatExemptionCode);
    }

    /// <summary>Copie d'un article du catalogue (F4 sur la référence) : la ligne garde ses quantité et remise.</summary>
    public DocumentLineRow WithCatalogItem(CatalogItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return this with
        {
            CatalogItemId = item.Id,
            Reference = item.Reference,
            Designation = item.Designation,
            Description = item.Description,
            Kind = item.Kind,
            UnitCode = item.UnitCode,
            UnitPriceExclTax = item.UnitPriceExclTax,
            VatRateCode = item.VatRateCode,
            VatRatePercent = item.VatRatePercent,
            VatExemptionCode = null,
        };
    }

    /// <summary>
    /// Ligne en cours de saisie, sans désignation : elle reste à l'écran mais n'est pas envoyée avec le brouillon, que le
    /// serveur refuserait en entier (désignation obligatoire). Elle part au premier enregistrement qui suit sa désignation.
    /// </summary>
    public bool IsIncomplete => string.IsNullOrWhiteSpace(Designation);

    /// <summary>Taux réellement appliqué : 0 % si la ligne est exonérée ou si le régime du document n'est pas standard.</summary>
    public decimal EffectiveVatPercent(VatRegimeDto regime) =>
        regime != VatRegimeDto.Standard || VatExemptionCode is not null ? 0m : VatRatePercent;

    public LineInput ToInput(VatRegimeDto regime) => new(Quantity, UnitPriceExclTax, EffectiveVatPercent(regime), DiscountPercent);

    /// <summary>Montant HT de la ligne par la règle d'arrondi unique ; 0 tant que la remise est hors bornes.</summary>
    public decimal TotalExclTax => DiscountPercent is < 0m or > 100m ? 0m : LineAmounts.TotalExclTax(ToInput(VatRegimeDto.Standard));

    public DocumentLineRequest ToRequest() => new()
    {
        CatalogItemId = CatalogItemId,
        Reference = string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
        Designation = Designation.Trim(),
        Description = Description,
        Kind = Kind,
        UnitCode = UnitCode,
        Quantity = Quantity,
        UnitPriceExclTax = UnitPriceExclTax,
        DiscountPercent = DiscountPercent,
        VatRateCode = VatRateCode,
        VatExemptionCode = VatExemptionCode,
    };
}
