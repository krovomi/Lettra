using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lettra.Ai.Evals;

/// <summary>
/// Comparaison tolérante d'une réponse avec un texte attendu : casse, accents, apostrophes, espaces insécables et
/// écriture des montants (« 2 400,00 € » = « 2400.00 EUR » = « 2400 euros ») ne comptent pas.
/// </summary>
public static partial class AnswerMatcher
{
    /// <summary>Le texte attendu figure dans la réponse, sans être collé à une lettre ou un chiffre (« 36,99 » ne trouve pas « 136,99 »).</summary>
    public static bool Contains(string answer, string expected)
    {
        string haystack = Normalize(answer);
        string needle = Normalize(expected);
        if (needle.Length == 0)
        {
            return true;
        }

        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            bool startOk = i == 0 || !char.IsLetterOrDigit(needle[0]) || !char.IsLetterOrDigit(haystack[i - 1]);
            int end = i + needle.Length;
            bool endOk = end == haystack.Length || !char.IsLetterOrDigit(needle[^1]) || !char.IsLetterOrDigit(haystack[end]);
            if (startOk && endOk)
            {
                return true;
            }
        }

        return false;
    }

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Accents et casse : « Échéance » = « echeance ». Table explicite : la normalisation Unicode n'est pas
        // disponible en mode InvariantGlobalization.
        StringBuilder plain = new(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            plain.Append(c switch
            {
                'à' or 'â' or 'ä' or 'á' => "a",
                'ç' => "c",
                'é' or 'è' or 'ê' or 'ë' => "e",
                'î' or 'ï' or 'í' => "i",
                'ô' or 'ö' or 'ó' => "o",
                'ù' or 'û' or 'ü' or 'ú' => "u",
                'ÿ' => "y",
                'œ' => "oe",
                'æ' => "ae",
                '’' or '‘' => "'",
                _ => c.ToString(),
            });
        }

        string s = plain.ToString()
            .Replace(' ', ' ').Replace(' ', ' ');

        // Montants : séparateurs de milliers et décimales unifiés, zéros inutiles retirés.
        s = Number().Replace(s, m =>
        {
            string raw = m.Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            raw = m.Groups["dec"].Success
                ? raw[..^m.Groups["dec"].Length].Replace(".", string.Empty, StringComparison.Ordinal) + "." + m.Groups["dec"].Value[1..]
                : raw.Replace(".", string.Empty, StringComparison.Ordinal);
            return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal d)
                ? d.ToString("0.##", CultureInfo.InvariantCulture)
                : m.Value;
        });

        s = Currency().Replace(s, " eur");
        return Spaces().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"(?<!\d)\d{1,3}(?:[ .]\d{3})+(?<dec>[,.]\d+)?(?!\d)|(?<!\d)\d+(?<dec>[,.]\d+)?(?!\d)")]
    private static partial Regex Number();

    [GeneratedRegex(@"\s?(?:€|euros?\b)")]
    private static partial Regex Currency();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
