using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lettra.Calculations.Receipts;

/// <summary>
/// Ce que la lecture d'une pièce propose : montant TTC, date, TVA et, s'il est seul, le taux. Chaque valeur peut manquer ;
/// l'utilisateur relit tout et confirme avant le dépôt. <paramref name="VatComputed"/> : la TVA n'est pas écrite sur la
/// pièce, elle est déduite du total et du seul taux lu.
/// </summary>
public sealed record ReceiptReading(decimal? AmountInclTax, DateOnly? Date, decimal? VatAmount, decimal? VatRate, bool VatComputed = false)
{
    public static ReceiptReading Empty { get; } = new(null, null, null, null);

    public bool IsEmpty => AmountInclTax is null && Date is null && VatAmount is null;
}

/// <summary>
/// Lecture d'un ticket de caisse ou d'une facture française à partir du texte reconnu sur la photo (reconnaissance faite
/// sur l'appareil). Règles déterministes, sans modèle : le total est le montant d'une ligne « Total TTC », « Net à payer »,
/// « Total »… ; la TVA, la somme des lignes de TVA par taux (le plus petit montant de la ligne, ou la ligne « Total TVA ») ;
/// la date, la première date plausible. Les fautes courantes de la reconnaissance (O pour 0, espace dans un nombre) sont
/// tolérées. Rien n'est inventé : une valeur introuvable reste vide.
/// </summary>
public static partial class ReceiptTextReader
{
    /// <summary>Taux de TVA français (métropole, Corse et outre-mer), en pourcentage.</summary>
    public static readonly IReadOnlyList<decimal> KnownVatRates = [20m, 10m, 8.5m, 5.5m, 2.1m, 13m, 0.9m];

    private const decimal MaxAmount = 1_000_000m;

    /// <summary>Mots d'une ligne de total, du plus sûr au moins sûr.</summary>
    private static readonly string[][] TotalKeywords =
    [
        ["TOTAL TTC", "MONTANT TTC", "NET A PAYER", "TOTAL A PAYER", "TTC A PAYER", "MONTANT A PAYER", "SOMME A PAYER"],
        ["TOTAL EUR", "TOTAL", "A PAYER", "NET"],
        ["CB", "CARTE", "ESPECES", "PAIEMENT", "REGLEMENT", "MONTANT"],
    ];

    private static readonly string[] Months =
        ["JANVIER", "FEVRIER", "MARS", "AVRIL", "MAI", "JUIN", "JUILLET", "AOUT", "SEPTEMBRE", "OCTOBRE", "NOVEMBRE", "DECEMBRE"];

    /// <summary>Lit le texte reconnu ; <paramref name="today"/> écarte les dates dans le futur.</summary>
    public static ReceiptReading Read(string? text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReceiptReading.Empty;
        }

        List<string> lines = [.. text.Split('\n').Select(Normalize).Where(l => l.Length > 0)];
        decimal? total = Total(lines);
        (decimal? vat, decimal? rate) = Vat(lines, total);
        bool computed = false;
        if (vat is null && rate is { } single && total is { } t)
        {
            vat = Rounding.ToCents(t - (t / (1 + (single / 100m))));
            computed = true;
        }

        return new ReceiptReading(total, FirstDate(lines, today), vat, rate, computed);
    }

    /// <summary>Majuscules sans accents, espaces simples, et « O » lu à la place de « 0 » au milieu des chiffres.</summary>
    internal static string Normalize(string line)
    {
        StringBuilder text = new(line.Length);
        foreach (char c in line.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                text.Append(char.ToUpperInvariant(c));
            }
        }

        string upper = SpacesRegex().Replace(text.ToString().Replace(' ', ' ').Replace('€', ' '), " ").Trim();
        return DigitORegex().Replace(upper, m => m.Value.Replace('O', '0'));
    }

    /// <summary>Les montants d'une ligne, dans l'ordre : « 1 234,56 », « 12.50 », « 3,3 » écartés (deux décimales exigées).</summary>
    internal static List<decimal> Amounts(string line)
    {
        List<decimal> amounts = [];
        foreach (Match m in AmountRegex().Matches(line))
        {
            string digits = m.Groups["int"].Value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);
            if (decimal.TryParse($"{digits}.{m.Groups["dec"].Value}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value)
                && value is > 0m and < MaxAmount)
            {
                amounts.Add(m.Groups["sign"].Success ? -value : value);
            }
        }

        return amounts;
    }

    private static decimal? Total(List<string> lines)
    {
        foreach (string[] keywords in TotalKeywords)
        {
            List<decimal> found =
            [
                .. lines.Where(l => keywords.Any(k => HasWord(l, k)) && !IsVatOrNetLine(l))
                    .SelectMany(l => Amounts(l).Where(a => a > 0m).TakeLast(1)),
            ];
            if (found.Count > 0)
            {
                return found.Max();
            }
        }

        List<decimal> all = [.. lines.Where(l => !IsVatOrNetLine(l)).SelectMany(Amounts).Where(a => a > 0m)];
        return all.Count > 0 ? all.Max() : null;
    }

    /// <summary>Une ligne de TVA ou de montant hors taxe ne donne jamais le total (sauf si elle dit aussi TTC).</summary>
    private static bool IsVatOrNetLine(string line) =>
        !line.Contains("TTC", StringComparison.Ordinal) && (HasWord(line, "TVA") || HasWord(line, "HT") || HasWord(line, "H.T") || line.Contains("HORS TAXE", StringComparison.Ordinal));

    private static (decimal? Vat, decimal? Rate) Vat(List<string> lines, decimal? total)
    {
        HashSet<decimal> rates = [];
        // « Total TVA 3,33 » donne directement la TVA.
        foreach (string line in lines.Where(l => l.Contains("TOTAL TVA", StringComparison.Ordinal) || l.Contains("MONTANT TVA", StringComparison.Ordinal)
            || l.Contains("DONT TVA", StringComparison.Ordinal)))
        {
            decimal vat = Amounts(line).LastOrDefault(a => a > 0m && (total is null || a < total));
            if (vat > 0m)
            {
                return (vat, RatesOf(line).Count == 1 ? RatesOf(line)[0] : null);
            }
        }

        // Sinon, une ligne par taux : « TVA 20,00% 3,33 » ou, sous un en-tête « Taux HT TVA TTC » (« Base » ou « Taux » y
        // figure : un en-tête de tableau d'articles n'en est pas un), « 20% 16,67 3,33 20,00 ».
        bool inTable = false;
        decimal sum = 0m;
        int rows = 0;
        foreach (string line in lines)
        {
            bool vatWord = HasWord(line, "TVA") && !IsVatNumberLine(line);
            if (vatWord && (HasWord(line, "TAUX") || HasWord(line, "BASE")) && RatesOf(line).Count == 0 && Amounts(line).Count == 0)
            {
                inTable = true;
                continue;
            }

            IReadOnlyList<decimal> lineRates = RatesOf(line);
            if (lineRates.Count == 1 && (vatWord || inTable))
            {
                rates.Add(lineRates[0]);
                List<decimal> amounts = [.. Amounts(WithoutRates(line)).Where(a => a > 0m && (total is null || a < total))];
                if (amounts.Count > 0)
                {
                    sum += amounts.Min();
                    rows++;
                }
            }
            else if (inTable && Amounts(line).Count == 0 && lineRates.Count == 0)
            {
                inTable = false;
            }
        }

        decimal? rate = rates.Count == 1 ? rates.First() : null;
        return rows > 0 && (total is null || sum < total) ? (sum, rate) : (null, rate);
    }

    /// <summary>« N° TVA intracommunautaire FR12345678901 » n'est pas une ligne de taxe.</summary>
    private static bool IsVatNumberLine(string line) =>
        line.Contains("INTRA", StringComparison.Ordinal) || VatNumberRegex().IsMatch(line) || line.Contains("N° TVA", StringComparison.Ordinal)
        || line.Contains("NO TVA", StringComparison.Ordinal);

    /// <summary>Les taux connus écrits sur la ligne (« 20% », « 5,5 % », « 10.00% »).</summary>
    internal static IReadOnlyList<decimal> RatesOf(string line)
    {
        List<decimal> rates = [];
        foreach (Match m in RateRegex().Matches(line))
        {
            if (decimal.TryParse(m.Groups["rate"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal rate)
                && KnownVatRates.Contains(rate) && !rates.Contains(rate))
            {
                rates.Add(rate);
            }
        }

        return rates;
    }

    private static string WithoutRates(string line) => RateRegex().Replace(line, " ");

    private static DateOnly? FirstDate(List<string> lines, DateOnly today)
    {
        DateOnly oldest = today.AddYears(-6);
        foreach (string line in lines)
        {
            foreach (Match m in NumericDateRegex().Matches(line))
            {
                int year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                if (Valid(year < 100 ? 2000 + year : year, Int(m.Groups["m"].Value), Int(m.Groups["d"].Value), oldest, today) is { } date)
                {
                    return date;
                }
            }

            foreach (Match m in IsoDateRegex().Matches(line))
            {
                if (Valid(Int(m.Groups["y"].Value), Int(m.Groups["m"].Value), Int(m.Groups["d"].Value), oldest, today) is { } date)
                {
                    return date;
                }
            }

            foreach (Match m in WordDateRegex().Matches(line))
            {
                int month = Array.FindIndex(Months, n => m.Groups["m"].Value.StartsWith(n[..3], StringComparison.Ordinal)) + 1;
                if (month > 0 && Valid(Int(m.Groups["y"].Value), month, Int(m.Groups["d"].Value), oldest, today) is { } date)
                {
                    return date;
                }
            }
        }

        return null;
    }

    private static int Int(string digits) => int.Parse(digits, CultureInfo.InvariantCulture);

    private static DateOnly? Valid(int year, int month, int day, DateOnly oldest, DateOnly today)
    {
        if (month is < 1 or > 12 || day < 1 || year is < 1 or > 9999 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        DateOnly date = new(year, month, day);
        return date >= oldest && date <= today ? date : null;
    }

    private static bool HasWord(string line, string word)
    {
        int at = line.IndexOf(word, StringComparison.Ordinal);
        while (at >= 0)
        {
            bool start = at == 0 || !char.IsLetter(line[at - 1]);
            bool end = at + word.Length >= line.Length || !char.IsLetter(line[at + word.Length]);
            if (start && end)
            {
                return true;
            }

            at = line.IndexOf(word, at + 1, StringComparison.Ordinal);
        }

        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    /// <summary>Un « O » collé à des chiffres (« 1O,5O »), que la reconnaissance confond avec un zéro.</summary>
    [GeneratedRegex(@"(?<=\d)O+|O+(?=\d)|(?<=[,.])O+")]
    private static partial Regex DigitORegex();

    [GeneratedRegex(@"(?<sign>-\s?)?(?<![\d,./])(?<int>\d{1,3}(?:[ .]\d{3})+|\d{1,7})\s?[,.]\s?(?<dec>\d{2})(?![\d%])(?![.,/\-]\d)(?!\s?%)")]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(?<![\d,.])(?<rate>\d{1,2}(?:[,.]\d{1,2})?)\s?%")]
    private static partial Regex RateRegex();

    [GeneratedRegex(@"FR\s?[0-9A-Z]{2}\s?\d{3}\s?\d{3}\s?\d{3}")]
    private static partial Regex VatNumberRegex();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})[/.\-](?<m>\d{1,2})[/.\-](?<y>\d{4}|\d{2})(?!\d)")]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"(?<!\d)(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?!\d)")]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})(?:ER)?\s+(?<m>[A-Z]{3,9})\.?\s+(?<y>\d{4})(?!\d)")]
    private static partial Regex WordDateRegex();
}
