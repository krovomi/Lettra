namespace Lettra.Calculations;

/// <summary>Une ligne de document telle que la voient les calculs : quantité, prix unitaire HT, taux de TVA.</summary>
/// <param name="Quantity">Quantité (peut être négative sur un avoir).</param>
/// <param name="UnitPriceExclTax">Prix unitaire hors taxes.</param>
/// <param name="VatRatePercent">Taux de TVA en pourcentage (20, 10, 5.5, 2.1, 0).</param>
/// <param name="DiscountPercent">Remise en pourcentage appliquée à la ligne (0 à 100).</param>
public readonly record struct LineInput(
    decimal Quantity,
    decimal UnitPriceExclTax,
    decimal VatRatePercent,
    decimal DiscountPercent = 0m);

/// <summary>Montant HT d'une ligne, arrondi au centime.</summary>
public static class LineAmounts
{
    public static decimal TotalExclTax(LineInput line)
    {
        if (line.DiscountPercent is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(line), line.DiscountPercent, "La remise doit être comprise entre 0 et 100 %.");
        }

        decimal gross = line.Quantity * line.UnitPriceExclTax;
        decimal discounted = gross * (100m - line.DiscountPercent) / 100m;
        return Rounding.ToCents(discounted);
    }
}
