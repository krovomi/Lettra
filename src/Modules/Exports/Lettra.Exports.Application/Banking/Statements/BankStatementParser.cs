using System.Globalization;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Banking.Statements;

/// <summary>
/// Lecture d'un relevé bancaire, sans connexion à la banque : le format se reconnaît au contenu (CAMT.053, OFX), un
/// autre fichier texte est lu comme un CSV. Rien n'est enregistré ici.
/// </summary>
public static class BankStatementParser
{
    /// <summary>5 Mo : plusieurs années d'opérations d'une petite entreprise.</summary>
    public const int MaxContentLength = 5 * 1024 * 1024;

    public const int MaxTransactions = 10_000;

    public static Result<ParsedBankStatement> Parse(ReadOnlySpan<byte> content, CsvImportOptions? csv)
    {
        if (content.IsEmpty)
        {
            return BankStatementErrors.FileRequired;
        }

        if (content.Length > MaxContentLength)
        {
            return BankStatementErrors.FileTooLarge;
        }

        string text = Decode(content);
        string head = text.Length > 4096 ? text[..4096] : text;
        Result<ParsedBankStatement> parsed;
        if (head.Contains("camt.053", StringComparison.OrdinalIgnoreCase) || head.Contains("BkToCstmrStmt", StringComparison.Ordinal))
        {
            parsed = Camt053Reader.Read(text);
        }
        else if (head.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase) || head.Contains("<OFX>", StringComparison.OrdinalIgnoreCase))
        {
            parsed = OfxReader.Read(text);
        }
        else if (head.TrimStart().StartsWith('<'))
        {
            return BankStatementErrors.FormatUnknown;
        }
        else
        {
            parsed = CsvStatementReader.Read(text, csv);
        }

        if (parsed.IsFailure)
        {
            return parsed;
        }

        if (parsed.Value.Transactions.Count == 0)
        {
            return BankStatementErrors.NoTransactions;
        }

        return parsed.Value.Transactions.Count > MaxTransactions ? BankStatementErrors.FileTooLarge : parsed;
    }

    /// <summary>UTF-8 (avec ou sans BOM) s'il est valide, sinon Latin-1 (fichiers des banques en Windows-1252).</summary>
    internal static string Decode(ReadOnlySpan<byte> content)
    {
        try
        {
            string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    /// <summary>
    /// Montant écrit à la française ou à l'anglaise : « 1 234,56 », « -1234.56 », « 1.234,56 », « +120,00 € ».
    /// </summary>
    internal static bool TryParseAmount(string? raw, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        StringBuilder kept = new();
        foreach (char c in raw.Trim())
        {
            if (char.IsAsciiDigit(c) || c is ',' or '.' or '-' or '+')
            {
                kept.Append(c);
            }
            else if (c == '(')
            {
                kept.Append('-');
            }
        }

        string s = kept.ToString();
        int comma = s.LastIndexOf(',');
        int dot = s.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            // Le dernier séparateur est le séparateur décimal, l'autre groupe les milliers.
            s = comma > dot ? s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.') : s.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (comma >= 0)
        {
            s = s.Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && decimal.Round(amount, 2) == amount;
    }

    internal static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyyMMdd"];

    internal static bool TryParseDate(string? raw, string? format, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim();
        if (value.Length > 10 && value[10] is 'T' or ' ')
        {
            value = value[..10];
        }

        return format is { Length: > 0 }
            ? DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            : DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
