using System.Globalization;
using System.Text;

namespace Lettra.UI.Shared.Typing;

/// <summary>
/// Nettoyage d'un texte libre à la sortie du champ (vague 5, lane F) : espaces au bord et en double retirés (les retours
/// à la ligne sont gardés), pas d'espace avant une virgule ou un point, majuscule en tête de texte. Rien d'autre : le
/// texte reste celui de l'utilisateur. Jamais appliqué aux codes ni aux numéros.
/// </summary>
public static class TextTidy
{
    public static string Apply(string? text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        StringBuilder builder = new(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(TidyLine(lines[i]));
        }

        string result = builder.ToString().Trim();
        int first = result.IndexOf(result.FirstOrDefault(char.IsLetter));
        if (first >= 0 && result.Length > 0 && char.IsLower(result[first]) && !StartsWithCode(result, first))
        {
            result = string.Concat(result.AsSpan(0, first), char.ToUpper(result[first], culture).ToString(), result.AsSpan(first + 1));
        }

        return result;
    }

    private static string TidyLine(string line)
    {
        StringBuilder builder = new(line.Length);
        bool space = false;
        foreach (char c in line.Trim())
        {
            if (c is ' ' or '\t')
            {
                space = true;
                continue;
            }

            if (space && c is not (',' or '.'))
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Un mot avec des chiffres (« iPhone », « e-mail » à part) n'est pas mis en majuscule : « m3 de béton ».</summary>
    private static bool StartsWithCode(string text, int start)
    {
        int end = text.IndexOf(' ', start);
        ReadOnlySpan<char> word = end < 0 ? text.AsSpan(start) : text.AsSpan(start, end - start);
        foreach (char c in word)
        {
            if (char.IsDigit(c) || char.IsUpper(c))
            {
                return true;
            }
        }

        return false;
    }
}
