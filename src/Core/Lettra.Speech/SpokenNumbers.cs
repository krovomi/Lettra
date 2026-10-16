using System.Globalization;

namespace Lettra.Speech;

/// <summary>
/// Lecture des nombres d'une transcription, en chiffres (<c>120</c>, <c>1,5</c>, <c>1 200</c>) ou en toutes lettres
/// (« cent vingt », « quatre-vingt-dix-neuf », « deux virgule cinq », « une heure et demie », « one hundred and five »).
/// La lecture s'arrête au premier mot qui ne prolonge pas le nombre : « deux trois » donne 2, pas 5.
/// </summary>
internal static class SpokenNumbers
{
    private static readonly Dictionary<string, int> FrenchUnits = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5, ["six"] = 6,
        ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9, ["dix"] = 10, ["onze"] = 11, ["douze"] = 12, ["treize"] = 13,
        ["quatorze"] = 14, ["quinze"] = 15, ["seize"] = 16, ["vingt"] = 20, ["vingts"] = 20, ["trente"] = 30,
        ["quarante"] = 40, ["cinquante"] = 50, ["soixante"] = 60, ["septante"] = 70, ["huitante"] = 80, ["octante"] = 80,
        ["nonante"] = 90,
    };

    private static readonly Dictionary<string, int> EnglishUnits = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20,
        ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static Dictionary<string, int> Units(SpokenLanguage language) =>
        language == SpokenLanguage.English ? EnglishUnits : FrenchUnits;

    private static bool IsHundred(SpokenLanguage language, string key) =>
        language == SpokenLanguage.English ? key is "hundred" or "hundreds" : key is "cent" or "cents";

    private static bool IsThousand(SpokenLanguage language, string key) =>
        language == SpokenLanguage.English ? key is "thousand" or "thousands" : key is "mille" or "milles";

    private static bool IsMillion(SpokenLanguage language, string key) =>
        language == SpokenLanguage.English ? key is "million" or "millions" : key is "million" or "millions";

    private static bool IsHalf(SpokenLanguage language, string key) =>
        language == SpokenLanguage.English ? key == "half" : key is "demi" or "demie";

    private static string DecimalWord(SpokenLanguage language) => language == SpokenLanguage.English ? "point" : "virgule";

    private static string And(SpokenLanguage language) => language == SpokenLanguage.English ? "and" : "et";

    /// <summary>Le mot à cette position commence-t-il un nombre ?</summary>
    public static bool StartsNumber(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language) =>
        TryRead(tokens, index, language, out _, out _);

    /// <summary>Lit un nombre à partir de <paramref name="index"/> ; <paramref name="next"/> est le premier jeton après lui.</summary>
    public static bool TryRead(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language, out decimal value, out int next)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        value = 0;
        next = index;
        if (index >= tokens.Count)
        {
            return false;
        }

        // « demi-heure », « une demi-journée », « half » : un demi, l'unité suit.
        if (ReadLeadingHalf(tokens, index, language) is int afterHalf)
        {
            value = 0.5m;
            next = afterHalf;
            return true;
        }

        decimal total = 0;
        decimal current = 0;
        int? lastAdditive = null;
        bool lastWasQuatre = false;
        bool consumed = false;
        int i = index;
        while (i < tokens.Count)
        {
            SpokenToken token = tokens[i];
            if (token.Kind == TokenKind.Number)
            {
                // Un nombre en chiffres ne se colle qu'au début ou après « mille » (« 2 mille », « mille 200 »).
                if (consumed && (current != 0 || lastAdditive is not null))
                {
                    break;
                }

                decimal digits = ParseDigits(token.Text);
                i++;
                // « 1 200 » : groupes de trois chiffres séparés par une espace.
                while (digits == decimal.Truncate(digits) && i < tokens.Count && tokens[i].Kind == TokenKind.Number
                       && tokens[i].Text.Length == 3 && tokens[i].Text.All(char.IsAsciiDigit) && tokens[i].Start - tokens[i - 1].End == 1)
                {
                    digits = (digits * 1000) + ParseDigits(tokens[i].Text);
                    i++;
                }

                current += digits;
                consumed = true;
                lastAdditive = digits == decimal.Truncate(digits) && digits < 100 ? (int)digits : -1;
                lastWasQuatre = false;
                if (digits != decimal.Truncate(digits))
                {
                    break;
                }

                continue;
            }

            if (token.Kind == TokenKind.Symbol && token.IsSymbol('-') && consumed && i + 1 < tokens.Count && IsNumberWord(tokens[i + 1], language))
            {
                // Trait d'union entre deux mots du nombre : « dix-sept », « quatre-vingt-dix ».
                i++;
                continue;
            }

            if (token.Kind != TokenKind.Word)
            {
                break;
            }

            string key = token.Key;
            if (Units(language).TryGetValue(key, out int unit))
            {
                if (key is "vingt" or "vingts" && lastWasQuatre && current % 100 == 4)
                {
                    current += 76; // quatre-vingt = 4 × 20
                    lastAdditive = 80;
                    lastWasQuatre = false;
                    consumed = true;
                    i++;
                    continue;
                }

                if (consumed && !CanAdd(current, lastAdditive, unit))
                {
                    break;
                }

                current += unit;
                lastAdditive = unit;
                lastWasQuatre = unit == 4;
                consumed = true;
                i++;
                continue;
            }

            if (IsHundred(language, key))
            {
                if (current % 1000 >= 10)
                {
                    break;
                }

                decimal hundreds = current % 1000;
                current = current - hundreds + ((hundreds == 0 ? 1 : hundreds) * 100);
                lastAdditive = null;
                lastWasQuatre = false;
                consumed = true;
                i++;
                continue;
            }

            if (IsThousand(language, key))
            {
                total += (current == 0 ? 1 : current) * 1000;
                current = 0;
                lastAdditive = null;
                lastWasQuatre = false;
                consumed = true;
                i++;
                continue;
            }

            if (IsMillion(language, key) && consumed)
            {
                total = (total + current) * 1_000_000;
                current = 0;
                lastAdditive = null;
                lastWasQuatre = false;
                i++;
                continue;
            }

            if (key == And(language) && consumed && i + 1 < tokens.Count)
            {
                // « vingt et un », « vingt-et-un », « soixante et onze », « one hundred and five » ; « et demi » finit le nombre.
                int afterAnd = tokens[i + 1].IsSymbol('-') && i + 2 < tokens.Count ? i + 2 : i + 1;
                SpokenToken after = tokens[afterAnd];
                if (IsHalf(language, after.Key) || (language == SpokenLanguage.English && after.Key == "a" && i + 2 < tokens.Count && IsHalf(language, tokens[i + 2].Key)))
                {
                    break;
                }

                if (Units(language).TryGetValue(after.Key, out int joined) && CanAdd(current, lastAdditive, joined))
                {
                    i = afterAnd;
                    continue;
                }
            }

            break;
        }

        if (!consumed)
        {
            return false;
        }

        value = total + current;
        // Décimales dites : « deux virgule cinq », « 3 virgule 75 », « two point five ».
        if (i + 1 < tokens.Count && tokens[i].IsWord(DecimalWord(language)) && value == decimal.Truncate(value)
            && ReadFraction(tokens, i + 1, language) is (decimal fraction, int afterFraction))
        {
            value += fraction;
            i = afterFraction;
        }

        // « deux et demi », « two and a half ».
        if (ReadTrailingHalf(tokens, i, language) is int afterTrailing)
        {
            value += 0.5m;
            i = afterTrailing;
        }

        next = i;
        return true;
    }

    /// <summary>« et demi(e) » ou « and a half » à cette position : renvoie la position qui suit.</summary>
    public static int? ReadTrailingHalf(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (index + 1 >= tokens.Count || !tokens[index].IsWord(And(language)))
        {
            return null;
        }

        if (IsHalf(language, tokens[index + 1].Key))
        {
            return index + 2;
        }

        return language == SpokenLanguage.English && index + 2 < tokens.Count && tokens[index + 1].IsWord("a") && IsHalf(language, tokens[index + 2].Key)
            ? index + 3
            : null;
    }

    private static int? ReadLeadingHalf(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language)
    {
        int i = index;
        if (language == SpokenLanguage.French && tokens[i].Key is "un" or "une")
        {
            i++;
        }

        if (i < tokens.Count && tokens[i].Kind == TokenKind.Word && IsHalf(language, tokens[i].Key))
        {
            i++;
            return i < tokens.Count && tokens[i].IsSymbol('-') ? i + 1 : i;
        }

        return null;
    }

    private static (decimal Fraction, int Next)? ReadFraction(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language)
    {
        if (tokens[index].Kind == TokenKind.Number && tokens[index].Text.All(char.IsAsciiDigit))
        {
            return (ParseDigits("0." + tokens[index].Text), index + 1);
        }

        // Chiffre par chiffre (« zéro cinq », « cinq trois ») ; sinon un nombre entier (« cinquante », « vingt-cinq »).
        string digits = string.Empty;
        int i = index;
        while (i < tokens.Count && tokens[i].Kind == TokenKind.Word && Units(language).TryGetValue(tokens[i].Key, out int digit) && digit < 10)
        {
            digits += digit.ToString(CultureInfo.InvariantCulture);
            i++;
        }

        if (digits.Length >= 2)
        {
            return (ParseDigits("0." + digits), i);
        }

        if (TryRead(tokens, index, language, out decimal whole, out int next) && whole == decimal.Truncate(whole) && whole > 0)
        {
            return (ParseDigits("0." + ((long)whole).ToString(CultureInfo.InvariantCulture)), next);
        }

        return null;
    }

    private static bool IsNumberWord(SpokenToken token, SpokenLanguage language) =>
        token.Kind == TokenKind.Word
        && (Units(language).ContainsKey(token.Key) || IsHundred(language, token.Key) || IsThousand(language, token.Key) || token.Key == "et");

    /// <summary>
    /// Un mot s'ajoute au nombre en cours seulement après un multiplicateur (« cent vingt ») ou une dizaine
    /// (« trente-deux », « soixante-dix-neuf », « quatre-vingt-onze ») : « deux trois » reste deux nombres.
    /// </summary>
    private static bool CanAdd(decimal current, int? lastAdditive, int unit)
    {
        if (lastAdditive is null)
        {
            return current % 100 == 0 && unit < 100;
        }

        int last = lastAdditive.Value;
        if (last == 10)
        {
            // dix-sept, dix-huit, dix-neuf ; soixante-dix-sept, quatre-vingt-dix-neuf.
            return (current % 100 == 10 && unit is >= 7 and <= 9) || (current % 100 is 70 or 90 && unit is >= 1 and <= 9);
        }

        bool lastIsTen = last >= 20 && last % 10 == 0;
        if (!lastIsTen)
        {
            return false;
        }

        // Après soixante et quatre-vingt viennent aussi 10 à 19 (soixante-dix, quatre-vingt-dix-neuf).
        int limit = last is 60 or 80 ? 20 : 10;
        return unit > 0 && unit < limit;
    }

    private static decimal ParseDigits(string text) =>
        decimal.Parse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
