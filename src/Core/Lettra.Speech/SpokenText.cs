using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lettra.Speech;

internal enum TokenKind
{
    Word,
    Number,
    Symbol,
    LineBreak,
}

/// <summary>
/// Un mot, un nombre écrit en chiffres, un signe ou un saut de ligne de la transcription. <see cref="Key"/> sert aux
/// comparaisons (minuscules, sans accents) ; <see cref="Start"/> et <see cref="End"/> renvoient au texte d'origine,
/// pour qu'une désignation ou un nom de client garde exactement sa graphie.
/// </summary>
internal sealed record SpokenToken(TokenKind Kind, string Text, string Key, int Start, int End)
{
    public bool IsWord(string key) => Kind == TokenKind.Word && Key == key;

    public bool IsSymbol(char symbol) => Kind == TokenKind.Symbol && Text.Length == 1 && Text[0] == symbol;
}

/// <summary>Découpage et normalisation de la transcription d'un moteur vocal.</summary>
internal static partial class SpokenText
{
    /// <summary>
    /// Nombres en chiffres (<c>120</c>, <c>120,50</c>, <c>1.5</c>), élisions (<c>l'</c>, <c>d'</c>, <c>qu'</c>) gardées
    /// avec leur apostrophe, mots, sauts de ligne et tout autre signe isolé (ponctuation, trait d'union, €, %).
    /// </summary>
    [GeneratedRegex(@"(?<num>\d+(?:[.,]\d+)?)|(?<elision>\p{L}{1,5}['’](?=\p{L}))|(?<word>\p{L}+)|(?<nl>\n)|(?<sym>[^\s\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>Texte à découper : fins de ligne unifiées, espaces de bord retirés. Les positions des jetons s'y réfèrent.</summary>
    public static string Prepare(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    /// <summary>Découpe un texte passé par <see cref="Prepare"/>.</summary>
    public static List<SpokenToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<SpokenToken> tokens = [];
        foreach (Match match in TokenPattern().Matches(text))
        {
            TokenKind kind = match.Groups["num"].Success ? TokenKind.Number
                : match.Groups["elision"].Success || match.Groups["word"].Success ? TokenKind.Word
                : match.Groups["nl"].Success ? TokenKind.LineBreak
                : TokenKind.Symbol;
            tokens.Add(new SpokenToken(kind, match.Value, KeyOf(match.Value), match.Index, match.Index + match.Length));
        }

        return tokens;
    }

    /// <summary>Clé de comparaison : minuscules, sans accents, apostrophe droite.</summary>
    public static string KeyOf(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string decomposed = value.Replace('’', '\'').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder key = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                key.Append(c);
            }
        }

        return key.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Le texte d'origine entre deux jetons inclus, débarrassé de la ponctuation qui l'entoure.</summary>
    public static string Slice(string source, IReadOnlyList<SpokenToken> tokens, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tokens);
        while (first <= last && tokens[first].Kind == TokenKind.Symbol)
        {
            first++;
        }

        while (last >= first && tokens[last].Kind == TokenKind.Symbol)
        {
            last--;
        }

        return first > last ? string.Empty : source[tokens[first].Start..tokens[last].End].Trim();
    }
}
