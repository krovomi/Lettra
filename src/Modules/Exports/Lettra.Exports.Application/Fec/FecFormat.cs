using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lettra.Exports.Application.Fec;

/// <summary>
/// Le format du fichier des écritures comptables (FEC), article A47 A-1 du livre des procédures fiscales et
/// BOI-CF-IOR-60-40-20 : fichier texte à plat, 18 colonnes dans cet ordre, première ligne d'en-tête, séparateur
/// tabulation (ou barre verticale), dates <c>AAAAMMJJ</c>, montants à virgule décimale sans séparateur de milliers,
/// encodage ISO 8859-15. Nom du fichier : <c>&lt;SIREN&gt;FEC&lt;AAAAMMJJ&gt;</c> (date de clôture de l'exercice).
/// </summary>
public static partial class FecFormat
{
    public const string JournalCode = "JournalCode";
    public const string JournalLib = "JournalLib";
    public const string EcritureNum = "EcritureNum";
    public const string EcritureDate = "EcritureDate";
    public const string CompteNum = "CompteNum";
    public const string CompteLib = "CompteLib";
    public const string CompAuxNum = "CompAuxNum";
    public const string CompAuxLib = "CompAuxLib";
    public const string PieceRef = "PieceRef";
    public const string PieceDate = "PieceDate";
    public const string EcritureLib = "EcritureLib";
    public const string Debit = "Debit";
    public const string Credit = "Credit";
    public const string EcritureLet = "EcritureLet";
    public const string DateLet = "DateLet";
    public const string ValidDate = "ValidDate";
    public const string Montantdevise = "Montantdevise";
    public const string Idevise = "Idevise";

    /// <summary>Les 18 colonnes, dans l'ordre imposé.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        JournalCode, JournalLib, EcritureNum, EcritureDate, CompteNum, CompteLib, CompAuxNum, CompAuxLib, PieceRef,
        PieceDate, EcritureLib, Debit, Credit, EcritureLet, DateLet, ValidDate, Montantdevise, Idevise,
    ];

    /// <summary>Colonnes qui ne peuvent pas être vides.</summary>
    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.Ordinal)
    {
        JournalCode, JournalLib, EcritureNum, EcritureDate, CompteNum, CompteLib, PieceRef, PieceDate, EcritureLib,
        Debit, Credit, ValidDate,
    };

    public const char Separator = '\t';

    public const string LineEnd = "\r\n";

    public const string FileExtension = ".txt";

    /// <summary>ISO 8859-15 (latin-9) ; un caractère hors de la table devient « ? ».</summary>
    public static Encoding Encoding { get; } = CreateEncoding();

    /// <summary>Nom du fichier : SIREN, « FEC », date de clôture.</summary>
    public static string FileName(string siren, DateOnly closingDate) =>
        siren + "FEC" + FormatDate(closingDate) + FileExtension;

    public static string FormatDate(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Montant à deux décimales, virgule décimale, sans séparateur de milliers.</summary>
    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    public static bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0m;
        return AmountPattern().IsMatch(value)
            && decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>
    /// Texte libre d'un champ : sans séparateur ni fin de ligne, espaces normalisées, ponctuation typographique ramenée
    /// à son équivalent ISO 8859-15 pour qu'aucun libellé ne perde de caractère à l'encodage.
    /// </summary>
    public static string Clean(string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder text = new(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormC))
        {
            text.Append(c switch
            {
                '\t' or '\r' or '\n' or '|' or ' ' or ' ' or ' ' => ' ',
                '‘' or '’' or 'ʼ' => '\'',
                '“' or '”' => '"',
                '–' or '—' or '−' => '-',
                '…' => '.',
                _ when char.IsControl(c) => ' ',
                _ => c,
            });
        }

        string cleaned = Spaces().Replace(text.ToString(), " ").Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    [GeneratedRegex(@"^-?\d+(,\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(28605, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }
}
