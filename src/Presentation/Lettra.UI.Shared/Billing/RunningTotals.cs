using Lettra.Calculations;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Totaux d'un document tenus ligne par ligne (annexe A §4.2, point 4 bis) : une saisie retire l'ancienne contribution
/// de la ligne et ajoute la nouvelle, sans tout recalculer. La TVA reste calculée par taux sur la somme des bases HT
/// arrondies, exactement comme <see cref="DocumentCalculator"/> (même résultat, prouvé par les tests).
/// </summary>
public sealed class RunningTotals
{
    private readonly SortedDictionary<decimal, (decimal Base, int Lines)> _byRate = new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));

    public void Reset(IEnumerable<LineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        _byRate.Clear();
        foreach (LineInput line in lines)
        {
            Add(line);
        }
    }

    public void Add(LineInput line) => Apply(line.VatRatePercent, LineAmounts.TotalExclTax(line), +1);

    public void Remove(LineInput line) => Apply(line.VatRatePercent, -LineAmounts.TotalExclTax(line), -1);

    public void Replace(LineInput before, LineInput after)
    {
        Remove(before);
        Add(after);
    }

    public DocumentTotals Totals
    {
        get
        {
            List<VatBreakdownLine> breakdown = _byRate
                .Select(r => new VatBreakdownLine(r.Key, r.Value.Base, Rounding.ToCents(r.Value.Base * r.Key / 100m)))
                .ToList();
            decimal totalExclTax = breakdown.Sum(b => b.BaseExclTax);
            decimal totalVat = breakdown.Sum(b => b.VatAmount);
            return new DocumentTotals(breakdown, totalExclTax, totalVat, totalExclTax + totalVat);
        }
    }

    private void Apply(decimal rate, decimal amount, int lines)
    {
        (decimal current, int count) = _byRate.GetValueOrDefault(rate);
        count += lines;
        if (count <= 0)
        {
            _byRate.Remove(rate);
        }
        else
        {
            _byRate[rate] = (current + amount, count);
        }
    }
}
