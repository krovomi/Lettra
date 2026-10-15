namespace Lettra.Calculations;

/// <summary>Ventilation de la TVA pour une catégorie et un taux : base HT, montant de TVA.</summary>
public readonly record struct VatBreakdownLine(
    decimal VatRatePercent,
    decimal BaseExclTax,
    decimal VatAmount,
    string VatCategory = VatCategoryCodes.Standard);

/// <summary>Totaux d'un document : ventilation par taux, total HT, total TVA, total TTC.</summary>
public sealed record DocumentTotals(
    IReadOnlyList<VatBreakdownLine> Breakdown,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax);

/// <summary>
/// Calcul des totaux d'un document. La TVA est calculée <b>par taux</b> sur la somme
/// des bases HT arrondies, puis arrondie au centime : c'est la ventilation qui figure
/// sur la facture, et le total TTC en découle (jamais l'inverse).
/// <para>
/// Règle par défaut, <b>à faire valider par l'expert-comptable</b> (jalon J2) : chaque ligne HT arrondie au
/// centime, TVA calculée par taux sur le total HT du taux puis arrondie. C'est aussi la lecture des
/// contrôles EN 16931 (BR-CO-17). Une catégorie sans TVA (exonération, autoliquidation, hors champ)
/// garde sa base, avec une TVA nulle quel que soit le taux saisi.
/// </para>
/// </summary>
public static class DocumentCalculator
{
    public static DocumentTotals Compute(IEnumerable<LineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<VatBreakdownLine> breakdown = lines
            .GroupBy(l => (Category: l.VatCategory ?? VatCategoryCodes.Standard, Rate: VatCategoryCodes.IsTaxed(l.VatCategory) ? l.VatRatePercent : 0m))
            .OrderByDescending(g => g.Key.Rate)
            .ThenBy(g => g.Key.Category, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal baseExclTax = g.Sum(LineAmounts.TotalExclTax);
                decimal vat = Rounding.ToCents(baseExclTax * g.Key.Rate / 100m);
                return new VatBreakdownLine(g.Key.Rate, baseExclTax, vat, g.Key.Category);
            })
            .ToList();

        decimal totalExclTax = breakdown.Sum(b => b.BaseExclTax);
        decimal totalVat = breakdown.Sum(b => b.VatAmount);
        return new DocumentTotals(breakdown, totalExclTax, totalVat, totalExclTax + totalVat);
    }
}
