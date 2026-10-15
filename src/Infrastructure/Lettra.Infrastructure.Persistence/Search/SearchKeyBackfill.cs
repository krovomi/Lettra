using System.Globalization;
using System.Text;

namespace Lettra.Infrastructure.Persistence.Search;

/// <summary>
/// Remplissage de <c>SearchKey</c> pour les lignes écrites avant la colonne. Une migration ne peut pas appeler
/// <see cref="SearchText"/> : elle retire en SQL les accents de l'alphabet français et les ligatures, puis met en
/// majuscules (UPPER n'est appliqué qu'à de l'ASCII, identique sur les trois providers). Les lignes réécrites
/// ensuite passent par le calcul C#.
/// </summary>
public static class SearchKeyBackfill
{
    private static readonly (string From, string To)[] Replacements =
    [
        ("à", "a"), ("â", "a"), ("ä", "a"), ("á", "a"), ("ã", "a"), ("å", "a"),
        ("À", "A"), ("Â", "A"), ("Ä", "A"), ("Á", "A"), ("Ã", "A"), ("Å", "A"),
        ("ç", "c"), ("Ç", "C"),
        ("é", "e"), ("è", "e"), ("ê", "e"), ("ë", "e"), ("É", "E"), ("È", "E"), ("Ê", "E"), ("Ë", "E"),
        ("î", "i"), ("ï", "i"), ("í", "i"), ("ì", "i"), ("Î", "I"), ("Ï", "I"), ("Í", "I"), ("Ì", "I"),
        ("ñ", "n"), ("Ñ", "N"),
        ("ô", "o"), ("ö", "o"), ("ó", "o"), ("ò", "o"), ("õ", "o"), ("Ô", "O"), ("Ö", "O"), ("Ó", "O"), ("Ò", "O"), ("Õ", "O"),
        ("û", "u"), ("ù", "u"), ("ü", "u"), ("ú", "u"), ("Û", "U"), ("Ù", "U"), ("Ü", "U"), ("Ú", "U"),
        ("ÿ", "y"), ("ý", "y"), ("Ÿ", "Y"), ("Ý", "Y"),
        ("œ", "oe"), ("Œ", "OE"), ("æ", "ae"), ("Æ", "AE"), ("ß", "ss"),
        ("’", "'"),
    ];

    /// <summary>Remplacements appliqués par le SQL, dans l'ordre (vérifiés par un test contre <see cref="SearchText"/>).</summary>
    public static IReadOnlyList<(string From, string To)> HandledReplacements => Replacements;

    /// <summary>
    /// <c>UPDATE</c> du remplissage. Sur SQL Server (<paramref name="sqlServer"/>), littéraux Unicode (<c>N'…'</c>)
    /// et identifiants entre crochets ; ailleurs, identifiants entre guillemets.
    /// </summary>
    public static string Sql(string table, string sourceColumn, bool sqlServer = false)
    {
        string prefix = sqlServer ? "N" : string.Empty;
        string Quote(string identifier) => sqlServer ? $"[{identifier}]" : $"\"{identifier}\"";
        string column = Quote(sourceColumn);
        StringBuilder expression = new(column);
        foreach ((string from, string to) in Replacements)
        {
            expression.Insert(0, "REPLACE(");
            expression.Append(CultureInfo.InvariantCulture, $", {prefix}'{from}', '{to.Replace("'", "''", StringComparison.Ordinal)}')");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"UPDATE {Quote(table)} SET {Quote(SearchKey.PropertyName)} = UPPER({expression});");
    }
}
