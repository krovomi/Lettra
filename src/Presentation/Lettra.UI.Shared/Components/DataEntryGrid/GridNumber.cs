using System.Globalization;

namespace Lettra.UI.Shared.Components.DataEntryGrid;

/// <summary>Nombres saisis et affichés dans une grille, dans la culture de l'utilisateur (« 1 234,50 » en français).</summary>
public static class GridNumber
{
    /// <summary>Montant avec 2 à 4 décimales et séparateur de milliers de la culture.</summary>
    public static string FormatPrice(decimal value, CultureInfo culture) => value.ToString("#,##0.00##", culture);

    /// <summary>Montant arrondi au centime, séparateur de milliers de la culture.</summary>
    public static string FormatAmount(decimal value, CultureInfo culture) => value.ToString("#,##0.00", culture);

    /// <summary>
    /// Lit un nombre saisi : espaces (y compris insécables) ignorés comme séparateurs de milliers, symbole € toléré,
    /// et point accepté comme virgule décimale quand la culture utilise la virgule (pavé numérique).
    /// </summary>
    public static bool TryParse(string? text, CultureInfo culture, out decimal value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string compact = new(text.Where(ch => !char.IsWhiteSpace(ch) && ch != '€').ToArray());
        NumberFormatInfo format = culture.NumberFormat;
        string group = format.NumberGroupSeparator;
        // Séparateur de milliers visible (« , » en anglais) ; en français c'est une espace, déjà retirée.
        if (!string.IsNullOrWhiteSpace(group) && group.Trim() != format.NumberDecimalSeparator)
        {
            compact = compact.Replace(group.Trim(), string.Empty, StringComparison.Ordinal);
        }

        if (decimal.TryParse(compact, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, culture, out value))
        {
            return true;
        }

        return format.NumberDecimalSeparator == ","
            && !compact.Contains(',', StringComparison.Ordinal)
            && decimal.TryParse(compact.Replace('.', ','), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, culture, out value);
    }
}
