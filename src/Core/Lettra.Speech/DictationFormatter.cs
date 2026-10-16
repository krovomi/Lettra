using System.Text;

namespace Lettra.Speech;

/// <summary>
/// Met en forme une dictée pour un champ de saisie : ponctuation dite à voix haute (« virgule », « point
/// d'interrogation », « à la ligne »), montants et pourcentages (« cent vingt euros » → « 120,00 € »), typographie
/// française (espaces insécables) et majuscules en début de phrase. Les autres nombres restent tels que le moteur les
/// a écrits : « un client » ne devient pas « 1 client ».
/// </summary>
public static class DictationFormatter
{
    private enum PieceKind
    {
        Word,
        Punctuation,
        Opening,
        Closing,
        LineBreak,
    }

    private sealed record Piece(string Text, PieceKind Kind, bool SpaceBefore, bool Synthetic);

    private sealed record SpokenMark(string[] Keys, string Symbol);

    private static readonly SpokenMark[] French = Longest(
    [
        new(["points", "de", "suspension"], "…"),
        new(["point", "d'", "interrogation"], "?"),
        new(["point", "d'", "exclamation"], "!"),
        new(["point", "virgule"], ";"),
        new(["deux", "points"], ":"),
        new(["point", "final"], "."),
        new(["retour", "a", "la", "ligne"], "\n"),
        new(["a", "la", "ligne"], "\n"),
        new(["nouvelle", "ligne"], "\n"),
        new(["nouveau", "paragraphe"], "\n\n"),
        new(["ouvrez", "la", "parenthese"], "("),
        new(["ouvrir", "la", "parenthese"], "("),
        new(["ouvrez", "les", "parentheses"], "("),
        new(["fermez", "la", "parenthese"], ")"),
        new(["fermer", "la", "parenthese"], ")"),
        new(["fermez", "les", "parentheses"], ")"),
        new(["ouvrez", "les", "guillemets"], "«"),
        new(["ouvrir", "les", "guillemets"], "«"),
        new(["fermez", "les", "guillemets"], "»"),
        new(["fermer", "les", "guillemets"], "»"),
        new(["virgule"], ","),
        new(["point"], "."),
    ]);

    private static readonly SpokenMark[] English = Longest(
    [
        new(["question", "mark"], "?"),
        new(["exclamation", "mark"], "!"),
        new(["exclamation", "point"], "!"),
        new(["full", "stop"], "."),
        new(["new", "paragraph"], "\n\n"),
        new(["new", "line"], "\n"),
        new(["open", "parenthesis"], "("),
        new(["open", "bracket"], "("),
        new(["close", "parenthesis"], ")"),
        new(["close", "bracket"], ")"),
        new(["open", "quote"], "“"),
        new(["close", "quote"], "”"),
        new(["dot", "dot", "dot"], "…"),
        new(["ellipsis"], "…"),
        new(["semicolon"], ";"),
        new(["colon"], ":"),
        new(["comma"], ","),
        new(["period"], "."),
    ]);

    // « point » et « period » sont aussi des noms : « à ce point », « point de vente », « the trial period ».
    private static readonly HashSet<string> FrenchPointNounBefore = new(StringComparer.Ordinal)
    {
        "ce", "cet", "au", "un", "le", "du", "de", "des", "les", "mise", "bon", "quel", "tel", "meme", "son", "mon", "ton",
        "leur", "notre", "votre", "chaque", "premier", "second", "dernier", "seul", "sur", "en", "a", "tout",
    };

    private static readonly HashSet<string> FrenchPointNounAfter = new(StringComparer.Ordinal)
    {
        "de", "d'", "du", "des", "sur", "par", "fort", "faible", "noir", "culminant", "mort", "cle", "commun", "precis",
        "particulier", "important", "positif", "negatif", "relais", "presse",
    };

    private static readonly HashSet<string> EnglishPeriodNounBefore = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "this", "that", "each", "every", "trial", "grace", "payment", "billing", "notice", "same", "per",
        "accounting", "reporting", "fiscal", "tax", "waiting",
    };

    private static readonly HashSet<string> EnglishPeriodNounAfter = new(StringComparer.Ordinal)
    {
        "of", "from", "to", "ending", "end", "starting",
    };

    /// <summary>Le texte mis en forme ; une transcription vide donne une chaîne vide.</summary>
    public static string Format(string? transcript, SpokenLanguage language)
    {
        string source = SpokenText.Prepare(transcript);
        List<SpokenToken> tokens = SpokenText.Tokenize(source);
        List<Piece> pieces = [];
        bool afterSpokenMark = false;
        int i = 0;
        while (i < tokens.Count)
        {
            SpokenToken token = tokens[i];
            bool spaceBefore = i > 0 && token.Start > tokens[i - 1].End;

            if (token.Kind == TokenKind.Word && MatchMark(tokens, i, language) is (SpokenMark mark, int afterMark))
            {
                AddMark(pieces, mark.Symbol);
                afterSpokenMark = true;
                i = afterMark;
                continue;
            }

            if (token.Kind is TokenKind.Word or TokenKind.Number || token.IsSymbol('€'))
            {
                if (SpokenAmounts.TryReadEuros(tokens, i, language, out decimal amount, out int afterAmount))
                {
                    pieces.Add(new Piece(SpokenAmounts.FormatEuros(amount, language), PieceKind.Word, spaceBefore, Synthetic: false));
                    afterSpokenMark = false;
                    i = afterAmount;
                    continue;
                }

                if (SpokenAmounts.TryReadPercent(tokens, i, language, out decimal percent, out int afterPercent))
                {
                    pieces.Add(new Piece(SpokenAmounts.FormatPercent(percent, language), PieceKind.Word, spaceBefore, Synthetic: false));
                    afterSpokenMark = false;
                    i = afterPercent;
                    continue;
                }
            }

            switch (token.Kind)
            {
                case TokenKind.LineBreak:
                    AddMark(pieces, "\n");
                    afterSpokenMark = false;
                    i++;
                    continue;
                case TokenKind.Symbol when KindOf(token.Text) is PieceKind.Punctuation:
                    // La ponctuation que le moteur ajoute autour d'une ponctuation dictée est un doublon.
                    if (!afterSpokenMark)
                    {
                        string symbol = token.Text;
                        if (symbol == "." && i + 2 < tokens.Count && tokens[i + 1].IsSymbol('.') && tokens[i + 2].IsSymbol('.'))
                        {
                            symbol = "…";
                            i += 2;
                        }

                        AddMark(pieces, symbol);
                    }

                    i++;
                    continue;
                case TokenKind.Symbol when KindOf(token.Text) is PieceKind.Opening or PieceKind.Closing:
                    AddMark(pieces, token.Text);
                    i++;
                    continue;
                default:
                    pieces.Add(new Piece(token.Text, PieceKind.Word, spaceBefore, Synthetic: false));
                    afterSpokenMark = false;
                    i++;
                    continue;
            }
        }

        return Join(pieces, language);
    }

    private static (SpokenMark Mark, int Next)? MatchMark(List<SpokenToken> tokens, int index, SpokenLanguage language)
    {
        foreach (SpokenMark mark in language == SpokenLanguage.English ? English : French)
        {
            if (index + mark.Keys.Length > tokens.Count)
            {
                continue;
            }

            bool matches = true;
            for (int k = 0; k < mark.Keys.Length && matches; k++)
            {
                matches = tokens[index + k].Kind == TokenKind.Word && tokens[index + k].Key == mark.Keys[k];
            }

            if (matches && !IsNoun(tokens, index, mark, language))
            {
                return (mark, index + mark.Keys.Length);
            }
        }

        return null;
    }

    private static bool IsNoun(List<SpokenToken> tokens, int index, SpokenMark mark, SpokenLanguage language)
    {
        SpokenToken? before = index > 0 ? tokens[index - 1] : null;
        int afterIndex = index + mark.Keys.Length;
        SpokenToken? after = afterIndex < tokens.Count ? tokens[afterIndex] : null;
        string single = mark.Keys.Length == 1 ? mark.Keys[0] : string.Empty;
        if (single is "point" or "period")
        {
            if (before?.Kind == TokenKind.Number || after?.Kind == TokenKind.Number)
            {
                return true;
            }

            (HashSet<string> nounBefore, HashSet<string> nounAfter) = language == SpokenLanguage.English
                ? (EnglishPeriodNounBefore, EnglishPeriodNounAfter)
                : (FrenchPointNounBefore, FrenchPointNounAfter);
            return (before?.Kind == TokenKind.Word && nounBefore.Contains(before.Key))
                   || (after?.Kind == TokenKind.Word && nounAfter.Contains(after.Key));
        }

        // « les deux points de vente » : un nom, pas des deux-points.
        return mark.Symbol == ":" && language == SpokenLanguage.French && after?.Kind == TokenKind.Word && after.Key is "de" or "d'" or "du" or "des";
    }

    private static PieceKind KindOf(string symbol) => symbol switch
    {
        "," or "." or ";" or ":" or "!" or "?" or "…" => PieceKind.Punctuation,
        "(" or "[" or "«" or "“" => PieceKind.Opening,
        ")" or "]" or "»" or "”" => PieceKind.Closing,
        "\n" or "\n\n" => PieceKind.LineBreak,
        _ => PieceKind.Word,
    };

    /// <summary>Ajoute un signe en évitant les doublons : « Bonjour, virgule, » donne une seule virgule.</summary>
    private static void AddMark(List<Piece> pieces, string symbol)
    {
        PieceKind kind = KindOf(symbol);
        Piece? last = pieces.Count > 0 ? pieces[^1] : null;
        switch (kind)
        {
            case PieceKind.Punctuation when last is { Kind: PieceKind.Punctuation }:
                if (symbol == "," || symbol == last.Text || last.Text == "…")
                {
                    return;
                }

                if (last.Text is "," or ".")
                {
                    pieces.RemoveAt(pieces.Count - 1);
                }

                break;
            case PieceKind.Punctuation when last is null || last.Kind is PieceKind.LineBreak or PieceKind.Opening:
                return;
            case PieceKind.LineBreak:
                if (last is null)
                {
                    return;
                }

                if (last is { Kind: PieceKind.Punctuation, Text: "," })
                {
                    pieces.RemoveAt(pieces.Count - 1);
                }

                if (pieces.Count > 0 && pieces[^1].Kind == PieceKind.LineBreak)
                {
                    pieces[^1] = pieces[^1] with { Text = symbol.Length > pieces[^1].Text.Length ? symbol : pieces[^1].Text };
                    return;
                }

                break;
        }

        pieces.Add(new Piece(symbol, kind, SpaceBefore: true, Synthetic: true));
    }

    private static string Join(List<Piece> pieces, SpokenLanguage language)
    {
        while (pieces.Count > 0 && pieces[^1].Kind is PieceKind.LineBreak or PieceKind.Opening)
        {
            pieces.RemoveAt(pieces.Count - 1);
        }

        StringBuilder text = new();
        bool capitalize = true;
        Piece? previous = null;
        foreach (Piece piece in pieces)
        {
            if (previous is not null)
            {
                text.Append(Separator(previous, piece, language));
            }

            if (piece.Kind == PieceKind.Word && capitalize && piece.Text.Length > 0 && char.IsLower(piece.Text[0]))
            {
                text.Append(char.ToUpperInvariant(piece.Text[0])).Append(piece.Text.AsSpan(1));
            }
            else
            {
                text.Append(piece.Text);
            }

            if (piece.Kind == PieceKind.Word)
            {
                capitalize = false;
            }
            else if (piece.Kind == PieceKind.LineBreak || piece.Text is "." or "!" or "?" or "…")
            {
                capitalize = true;
            }

            previous = piece;
        }

        return text.ToString().Trim();
    }

    private static string Separator(Piece previous, Piece current, SpokenLanguage language)
    {
        bool french = language == SpokenLanguage.French;
        if (current.Kind == PieceKind.LineBreak || previous.Kind == PieceKind.LineBreak)
        {
            return string.Empty;
        }

        switch (current.Kind)
        {
            case PieceKind.Punctuation:
                return !french ? string.Empty : current.Text switch
                {
                    ";" or "!" or "?" => SpokenAmounts.NarrowNoBreakSpace.ToString(),
                    ":" => SpokenAmounts.NoBreakSpace.ToString(),
                    _ => string.Empty,
                };
            case PieceKind.Closing:
                return current.Text == "»" ? SpokenAmounts.NoBreakSpace.ToString() : string.Empty;
        }

        if (previous.Kind == PieceKind.Opening)
        {
            return previous.Text == "«" ? SpokenAmounts.NoBreakSpace.ToString() : string.Empty;
        }

        if (previous.Synthetic || current.Synthetic || previous.Kind is PieceKind.Punctuation or PieceKind.Closing)
        {
            return previous.Synthetic || current.Synthetic || current.SpaceBefore ? " " : string.Empty;
        }

        return current.SpaceBefore ? " " : string.Empty;
    }

    private static SpokenMark[] Longest(SpokenMark[] marks) => [.. marks.OrderByDescending(m => m.Keys.Length)];
}
