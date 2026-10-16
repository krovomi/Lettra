using System.Globalization;
using System.Text;

namespace Lettra.Speech;

/// <summary>Montants en euros et pourcentages dits à voix haute, et leur écriture typographique.</summary>
internal static class SpokenAmounts
{
    /// <summary>Espace fine insécable : séparateur des milliers et avant « ; ! ? » en français.</summary>
    public const char NarrowNoBreakSpace = ' ';

    /// <summary>Espace insécable : avant « € », « % » et « : », dans les guillemets français.</summary>
    public const char NoBreakSpace = ' ';

    public static bool IsEuro(SpokenToken token) =>
        token.IsSymbol('€') || (token.Kind == TokenKind.Word && token.Key is "euro" or "euros" or "eur");

    private static bool IsCents(SpokenToken token) =>
        token.Kind == TokenKind.Word && token.Key is "centime" or "centimes" or "cent" or "cents";

    /// <summary>
    /// « cent vingt euros », « 120 € », « 12 euros 50 », « 12 euros et 50 centimes », « 0,99 € ». Le nombre doit être
    /// suivi du mot ou du signe de l'euro ; « 50 » seul n'est pas un montant.
    /// </summary>
    public static bool TryReadEuros(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language, out decimal amount, out int next)
    {
        amount = 0;
        next = index;
        if (index < tokens.Count && tokens[index].IsSymbol('€') && SpokenNumbers.TryRead(tokens, index + 1, language, out decimal prefixed, out int afterPrefixed))
        {
            // « €120 » (anglais).
            amount = Math.Round(prefixed, 2);
            next = afterPrefixed;
            return true;
        }

        if (!SpokenNumbers.TryRead(tokens, index, language, out decimal value, out int i) || i >= tokens.Count || !IsEuro(tokens[i]))
        {
            return false;
        }

        i++;
        // Centimes : « et 50 centimes », « 50 centimes », ou « 12 euros 50 » en fin de groupe.
        int cents = i;
        if (cents < tokens.Count && tokens[cents].Key is "et" or "and")
        {
            cents++;
        }

        if (value == decimal.Truncate(value) && SpokenNumbers.TryRead(tokens, cents, language, out decimal centimes, out int afterCents)
            && centimes == decimal.Truncate(centimes) && centimes is > 0 and < 100)
        {
            bool named = afterCents < tokens.Count && IsCents(tokens[afterCents]);
            bool endsGroup = afterCents >= tokens.Count || tokens[afterCents].Kind != TokenKind.Word || IsTaxMarker(tokens[afterCents]);
            if (named || (endsGroup && cents == i))
            {
                value += centimes / 100;
                i = named ? afterCents + 1 : afterCents;
            }
        }

        amount = Math.Round(value, 2);
        next = i;
        return true;
    }

    /// <summary>« dix pour cent », « 10 % », « 12,5 pourcent », « ten percent ».</summary>
    public static bool TryReadPercent(IReadOnlyList<SpokenToken> tokens, int index, SpokenLanguage language, out decimal percent, out int next)
    {
        percent = 0;
        next = index;
        if (!SpokenNumbers.TryRead(tokens, index, language, out decimal value, out int i) || i >= tokens.Count)
        {
            return false;
        }

        if (tokens[i].IsSymbol('%') || (tokens[i].Kind == TokenKind.Word && tokens[i].Key is "pourcent" or "pourcents" or "percent"))
        {
            next = i + 1;
        }
        else if (i + 1 < tokens.Count && tokens[i].Key is "pour" or "per" && tokens[i + 1].Key is "cent" or "cents")
        {
            next = i + 2;
        }
        else
        {
            return false;
        }

        percent = value;
        return true;
    }

    /// <summary>« HT », « hors taxes » : <c>false</c> ; « TTC », « toutes taxes comprises » : <c>true</c>.</summary>
    public static bool? TryReadTaxMarker(IReadOnlyList<SpokenToken> tokens, int index, out int next)
    {
        next = index;
        if (index >= tokens.Count || tokens[index].Kind != TokenKind.Word)
        {
            return null;
        }

        string key = tokens[index].Key;
        string? following = index + 1 < tokens.Count ? tokens[index + 1].Key : null;
        string? third = index + 2 < tokens.Count ? tokens[index + 2].Key : null;
        switch (key)
        {
            case "ht" or "hors" when key == "ht" || following is "taxe" or "taxes":
                next = key == "ht" ? index + 1 : index + 2;
                return false;
            case "ttc":
                next = index + 1;
                return true;
            case "toutes" when following is "taxes" && third is "comprises":
                next = index + 3;
                return true;
            case "excluding" or "including" when following is "vat" or "tax" or "taxes":
                next = index + 2;
                return key == "including";
            default:
                return null;
        }
    }

    private static bool IsTaxMarker(SpokenToken token) => token.Kind == TokenKind.Word && token.Key is "ht" or "ttc" or "hors" or "toutes";

    /// <summary>Montant écrit : « 1 200,50 € » en français, « €1,200.50 » en anglais.</summary>
    public static string FormatEuros(decimal amount, SpokenLanguage language)
    {
        string number = FormatNumber(Math.Round(amount, 2), 2, language);
        return language == SpokenLanguage.English ? "€" + number : number + NoBreakSpace + "€";
    }

    /// <summary>Pourcentage écrit : « 12,5 % » en français, « 12.5% » en anglais.</summary>
    public static string FormatPercent(decimal percent, SpokenLanguage language)
    {
        // Division qui retire les zéros de fin : 12,50 s'écrit 12,5.
        decimal normalized = percent / 1.0000000000000000000000000000m;
        string number = FormatNumber(normalized, Math.Min((int)normalized.Scale, 4), language);
        return language == SpokenLanguage.English ? number + "%" : number + NoBreakSpace + "%";
    }

    /// <summary>Groupes de milliers et virgule décimale écrits à la main : le résultat ne dépend pas de la culture du poste.</summary>
    private static string FormatNumber(decimal value, int decimals, SpokenLanguage language)
    {
        string invariant = Math.Abs(value).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        string[] parts = invariant.Split('.');
        char group = language == SpokenLanguage.English ? ',' : NarrowNoBreakSpace;
        StringBuilder integer = new();
        for (int i = 0; i < parts[0].Length; i++)
        {
            if (i > 0 && (parts[0].Length - i) % 3 == 0)
            {
                integer.Append(group);
            }

            integer.Append(parts[0][i]);
        }

        string sign = value < 0 ? "-" : string.Empty;
        return parts.Length == 1
            ? sign + integer
            : sign + integer + (language == SpokenLanguage.English ? "." : ",") + parts[1];
    }
}
