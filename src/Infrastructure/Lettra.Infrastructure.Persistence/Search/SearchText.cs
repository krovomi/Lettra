using System.Globalization;
using System.Text;

namespace Lettra.Infrastructure.Persistence.Search;

/// <summary>
/// Forme de recherche d'un texte : sans accents, en majuscules, ligatures dépliées, espaces réduits.
/// Calculée en C# et stockée dans une colonne, elle donne le même résultat sur SQLite, PostgreSQL et
/// SQL Server, quelle que soit la collation (SQLite ne sait ni ignorer les accents, ni mettre en
/// majuscules hors ASCII). « Équipe Œnologie » et « equipe oenologie » ont la même forme.
/// </summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool pendingSpace = false;
        foreach (char c in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            switch (c)
            {
                case 'œ' or 'Œ':
                    builder.Append("OE");
                    break;
                case 'æ' or 'Æ':
                    builder.Append("AE");
                    break;
                case 'ß':
                    builder.Append("SS");
                    break;
                case '’' or 'ʼ' or '‘':
                    builder.Append('\'');
                    break;
                default:
                    builder.Append(char.ToUpperInvariant(c));
                    break;
            }
        }

        return builder.ToString();
    }
}
