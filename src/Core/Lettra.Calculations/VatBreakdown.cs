namespace Lettra.Calculations;

/// <summary>Ventilation de la TVA pour un taux : base HT, montant de TVA.</summary>
public readonly record struct VatBreakdownLine(decimal VatRatePercent, decimal BaseExclTax, decimal VatAmount);

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
/// </summary>
public static class DocumentCalculator
{
    public static DocumentTotals Compute(IEnumerable<LineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        List<VatBreakdownLine> breakdown = lines
            .GroupBy(l => l.VatRatePercent)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                decimal baseExclTax = g.Sum(LineAmounts.TotalExclTax);
                decimal vat = Rounding.ToCents(baseExclTax * g.Key / 100m);
                return new VatBreakdownLine(g.Key, baseExclTax, vat);
            })
            .ToList();

        decimal totalExclTax = breakdown.Sum(b => b.BaseExclTax);
        decimal totalVat = breakdown.Sum(b => b.VatAmount);
        return new DocumentTotals(breakdown, totalExclTax, totalVat, totalExclTax + totalVat);
    }
}
