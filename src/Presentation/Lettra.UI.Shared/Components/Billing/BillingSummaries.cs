using System.Globalization;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Components.Billing;

/// <summary>Résumés d'une ligne des blocs repliés d'une pièce (vague 5, lane E).</summary>
public static class BillingSummaries
{
    private const int ExcerptLength = 80;

    /// <summary>Les morceaux renseignés, séparés par un point médian.</summary>
    public static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string Lines(IStringLocalizer localizer, int count, decimal totalExclTax) => count == 0
        ? localizer["Billing.Summary.NoLines"].Value
        : string.Format(CultureInfo.CurrentCulture, localizer["Billing.Summary.Lines"], count,
            GridNumber.FormatAmount(totalExclTax, CultureInfo.CurrentCulture));

    /// <summary>Début d'un texte libre sur une ligne, ou <c>null</c> s'il est vide.</summary>
    public static string? Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= ExcerptLength ? line : string.Concat(line.AsSpan(0, ExcerptLength - 1), "…");
    }
}
