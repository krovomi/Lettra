using System.Globalization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Typing;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// La lecture des cellules chiffrées d'une ligne de pièce (quantité, prix, remise, TVA), sans dépendance à l'écran :
/// une saisie valide donne la ligne modifiée, une saisie invalide lève <see cref="FormatException"/> et la grille garde
/// la valeur précédente.
/// </summary>
public static class DocumentLineCells
{
    /// <summary>Quantité, saisie rapide comprise (« 3h », « 1,5 j », « 2x3 ») : l'unité de temps reconnue remplace celle de la ligne.</summary>
    public static DocumentLineRow ParseQuantity(DocumentLineRow row, string text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        return QuickEntry.TryParseQuantity(text, culture, out QuickValue quantity)
            ? row with { Quantity = quantity.Value, UnitCode = quantity.UnitCode ?? row.UnitCode }
            : throw new FormatException(text);
    }

    /// <summary>Prix unitaire, produit compris (« 2x45 »).</summary>
    public static DocumentLineRow ParsePrice(DocumentLineRow row, string text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        return QuickEntry.TryParseAmount(text, culture, out QuickValue price) && price.Value >= 0m
            ? row with { UnitPriceExclTax = price.Value }
            : throw new FormatException(text);
    }

    /// <summary>Remise en pourcentage, de 0 à 100 ; une cellule vide retire la remise.</summary>
    public static DocumentLineRow ParseDiscount(DocumentLineRow row, string text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(text);
        string value = text.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (value.Length == 0)
        {
            return row with { DiscountPercent = 0m };
        }

        return GridNumber.TryParse(value, culture, out decimal discount) && discount is >= 0m and <= 100m
            ? row with { DiscountPercent = discount }
            : throw new FormatException(text);
    }

    /// <summary>
    /// TVA : code (« FR-5.5 »), taux (« 5,5 », « 20 % ») ou motif d'exonération (« VATEX-EU-132 »), ce dernier en régime
    /// normal seulement.
    /// </summary>
    public static DocumentLineRow ParseVat(DocumentLineRow row, string text, BillingReferenceData referenceData, VatRegimeDto regime, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(referenceData);
        string value = text.Trim();
        VatExemptionReasonOption? exemption = referenceData.ExemptionReasons.FirstOrDefault(e => e.Code.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exemption is not null)
        {
            return regime == VatRegimeDto.Standard ? row with { VatExemptionCode = exemption.Code } : throw new FormatException(value);
        }

        VatRateOption? rate = referenceData.VatRates.FirstOrDefault(v => v.Code.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (rate is null && GridNumber.TryParse(value.Replace("%", string.Empty, StringComparison.Ordinal), culture, out decimal percent))
        {
            rate = referenceData.VatRates.FirstOrDefault(v => v.Percent == percent);
        }

        return rate is null
            ? throw new FormatException(value)
            : row with { VatRateCode = rate.Code, VatRatePercent = rate.Percent, VatExemptionCode = null };
    }
}
