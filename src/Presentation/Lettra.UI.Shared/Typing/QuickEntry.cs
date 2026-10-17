using System.Globalization;
using System.Text.RegularExpressions;
using Lettra.UI.Shared.Components.DataEntryGrid;

namespace Lettra.UI.Shared.Typing;

/// <summary>Résultat d'une saisie rapide : la valeur, l'unité reconnue (code CEE-ONU) et si un calcul a été fait.</summary>
public sealed record QuickValue(decimal Value, string? UnitCode, bool Computed);

/// <summary>
/// Saisie rapide des quantités et des montants (vague 5, lane F), sans IA : « 3h » vaut 3 heures, « 1h30 » 1,5 heure,
/// « 45 min » 0,75 heure, « 1,5 j » ou « 2 jours » des jours, « 2x45 », « 2 * 45 » ou « 3 × 2,5 » le produit. Les nombres
/// suivent la culture (<see cref="GridNumber.TryParse"/>) : « 1,5 » en français, « 1.5 » en anglais.
/// </summary>
public static partial class QuickEntry
{
    public const string Hour = "HUR";
    public const string Day = "DAY";

    /// <summary>Quantité, avec une unité de temps éventuelle.</summary>
    public static bool TryParseQuantity(string? text, CultureInfo culture, out QuickValue value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        value = new QuickValue(0m, null, false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string input = text.Trim().ToLower(culture);
        Match hm = HoursMinutes().Match(input);
        if (hm.Success)
        {
            decimal hours = decimal.Parse(hm.Groups["h"].Value, CultureInfo.InvariantCulture);
            decimal minutes = decimal.Parse(hm.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (minutes >= 60m)
            {
                return false;
            }

            value = new QuickValue(Math.Round(hours + (minutes / 60m), 4), Hour, true);
            return true;
        }

        Match unit = WithUnit().Match(input);
        if (unit.Success && TryProduct(unit.Groups["n"].Value, culture, out decimal amount, out bool computed))
        {
            (string code, decimal factor) = UnitOf(unit.Groups["u"].Value);
            value = new QuickValue(Math.Round(amount * factor, 4), code, computed || factor != 1m);
            return true;
        }

        if (TryProduct(input, culture, out decimal plain, out bool product))
        {
            value = new QuickValue(plain, null, product);
            return true;
        }

        return false;
    }

    /// <summary>Montant : un nombre ou un produit (« 2x45 »), symbole € toléré ; pas d'unité.</summary>
    public static bool TryParseAmount(string? text, CultureInfo culture, out QuickValue value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        value = new QuickValue(0m, null, false);
        if (string.IsNullOrWhiteSpace(text) || !TryProduct(text.Replace("€", string.Empty, StringComparison.Ordinal).Trim(), culture, out decimal amount, out bool computed))
        {
            return false;
        }

        value = new QuickValue(Math.Round(amount, 4), null, computed);
        return true;
    }

    /// <summary>La saisie est-elle une forme rapide (calcul ou unité) qui mérite un aperçu ?</summary>
    public static bool IsQuickForm(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(c => char.IsLetter(c) || c is '*' or '×');

    private static bool TryProduct(string text, CultureInfo culture, out decimal value, out bool computed)
    {
        string[] factors = text.Split(['x', 'X', '*', '×'], StringSplitOptions.TrimEntries);
        value = 1m;
        computed = factors.Length > 1;
        foreach (string factor in factors)
        {
            if (!GridNumber.TryParse(factor, culture, out decimal number))
            {
                value = 0m;
                return false;
            }

            value *= number;
        }

        return true;
    }

    private static (string Code, decimal Factor) UnitOf(string unit) => unit switch
    {
        "min" or "mn" or "minute" or "minutes" => (Hour, 1m / 60m),
        "j" or "jr" or "jour" or "jours" or "d" or "day" or "days" => (Day, 1m),
        _ => (Hour, 1m),
    };

    [GeneratedRegex(@"^(?<h>\d{1,4})\s*h\s*(?<m>\d{1,2})(\s*(min|mn))?$", RegexOptions.CultureInvariant)]
    private static partial Regex HoursMinutes();

    [GeneratedRegex(@"^(?<n>[\d\s.,x×*]+?)\s*(?<u>h|hr|hrs|heure|heures|hour|hours|min|mn|minute|minutes|j|jr|jour|jours|d|day|days)\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex WithUnit();
}
