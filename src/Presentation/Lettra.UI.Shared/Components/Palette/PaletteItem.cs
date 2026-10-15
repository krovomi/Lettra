using System.Globalization;
using System.Text;

namespace Lettra.UI.Shared.Components.Palette;

/// <summary>Entrée de la palette de commandes : libellé traduit, groupe (« Actions », « Documents »…), action.</summary>
public sealed record PaletteItem(string Group, string Label, Func<Task> Run, string? Hint = null);

/// <summary>Source d'entrées de la palette (Ctrl+K). Chaque source filtre elle-même avec <see cref="PaletteMatcher"/>.</summary>
public interface ICommandPaletteProvider
{
    Task<IReadOnlyList<PaletteItem>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>
/// Recherche floue de la palette : chaque mot saisi doit se retrouver dans le libellé, sans tenir compte des accents
/// ni de la casse (« facture dupont » trouve « Nouvelle facture pour Dupont SARL »). Un mot qui commence un mot du
/// libellé compte plus qu'un mot trouvé au milieu.
/// </summary>
public static class PaletteMatcher
{
    /// <summary>Score positif si tous les mots correspondent, 0 sinon. Une requête vide correspond à tout (score 1).</summary>
    public static int Score(string label, string query)
    {
        ArgumentNullException.ThrowIfNull(label);
        string[] words = Normalize(query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return 1;
        }

        string text = " " + Normalize(label);
        int score = 0;
        foreach (string word in words)
        {
            int index = text.IndexOf(word, StringComparison.Ordinal);
            if (index < 0)
            {
                return 0;
            }

            score += text[index - 1] == ' ' || !char.IsLetterOrDigit(text[index - 1]) ? 10 : 3;
        }

        return score + Math.Max(0, 5 - (text.Length / 20));
    }

    /// <summary>Minuscules sans accents, ponctuation ramenée à des espaces sauf « - » (numéros de pièces).</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString();
    }
}
