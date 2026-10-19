using System.Globalization;

namespace Lettra.UI.Shared.Components.Dates;

/// <summary>
/// Dates affichées et saisies dans la langue de l'écran : « 19/10/2026 » en français, « 10/19/2026 » en anglais.
/// Le champ de date du navigateur suit la langue du navigateur ou du système, pas celle de Lettra : d'où ce champ à nous.
/// La saisie accepte aussi la date ISO (2026-10-19), les chiffres seuls (19102026, 191026), le jour et le mois seuls
/// (année en cours) et n'importe quel séparateur (/ . - espace).
/// </summary>
public static class DateText
{
    private const string Iso = "yyyy-MM-dd";

    /// <summary>Date courte de la culture, ou texte vide.</summary>
    public static string Format(DateOnly? date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return date?.ToString("d", culture) ?? string.Empty;
    }

    /// <summary>
    /// Lit une date tapée. Vrai avec <c>null</c> pour un texte vide, faux pour un texte qui n'est pas une date.
    /// </summary>
    public static bool TryParse(string? text, CultureInfo culture, DateOnly today, out DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(culture);
        date = null;
        string input = text?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            return true;
        }

        if (DateOnly.TryParseExact(input, Iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly iso))
        {
            date = iso;
            return true;
        }

        if (input.Any(char.IsLetter))
        {
            bool named = DateOnly.TryParse(input, culture, DateTimeStyles.AllowWhiteSpaces, out DateOnly written);
            date = named ? written : null;
            return named;
        }

        string[] parts = Split(input, Order(culture).Year == 0);
        if (parts.Length is < 2 or > 3 || !parts.All(p => p.Length is > 0 and <= 4 && p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        DateParts order = Order(culture);
        bool dayFirst = order.Day < order.Month;
        int day = int.Parse(parts[parts.Length == 3 ? order.Day : (dayFirst ? 0 : 1)], CultureInfo.InvariantCulture);
        int month = int.Parse(parts[parts.Length == 3 ? order.Month : (dayFirst ? 1 : 0)], CultureInfo.InvariantCulture);
        int? year = parts.Length == 3 ? Year(parts[order.Year]) : today.Year;
        if (year is not { } y || y is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(y, month))
        {
            return false;
        }

        date = new DateOnly(y, month, day);
        return true;
    }

    /// <summary>Indique quelle partie de la date courte vient en premier (jour, mois ou année) dans la culture.</summary>
    public static DateParts Order(CultureInfo culture)
    {
        string pattern = culture.DateTimeFormat.ShortDatePattern;
        int[] positions = [Position(pattern, 'd'), Position(pattern, 'M'), Position(pattern, 'y')];
        int[] sorted = [.. positions.Order()];
        return new(Array.IndexOf(sorted, positions[0]), Array.IndexOf(sorted, positions[1]), Array.IndexOf(sorted, positions[2]));
    }

    private static int Position(string pattern, char letter)
    {
        int index = pattern.IndexOf(letter, StringComparison.Ordinal);
        return index < 0 ? int.MaxValue - letter : index;
    }

    /// <summary>Découpe aux séparateurs ; des chiffres seuls se coupent en 2 + 2 + 2 ou 4 (« 19102026 », « 191026 », « 1910 »).</summary>
    private static string[] Split(string input, bool yearFirst)
    {
        if (!input.All(char.IsAsciiDigit))
        {
            return input.Split(['/', '.', '-', ' ', ' ', ','], StringSplitOptions.RemoveEmptyEntries);
        }

        return input.Length switch
        {
            4 => [input[..2], input[2..]],
            6 => [input[..2], input[2..4], input[4..]],
            8 when yearFirst => [input[..4], input[4..6], input[6..]],
            8 => [input[..2], input[2..4], input[4..]],
            _ => [],
        };
    }

    /// <summary>Année sur 4 chiffres, ou sur 1 ou 2 chiffres lue comme 20xx.</summary>
    private static int? Year(string text) => text.Length switch
    {
        <= 2 => 2000 + int.Parse(text, CultureInfo.InvariantCulture),
        4 => int.Parse(text, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>Rang (0, 1 ou 2) du jour, du mois et de l'année dans la date courte.</summary>
    public readonly record struct DateParts(int Day, int Month, int Year);
}

/// <summary>Module JS du calendrier (placement à l'ouverture).</summary>
public static class DateModule
{
    public const string Path = "./_content/Lettra.UI.Shared/lettra-dates.js";
}
