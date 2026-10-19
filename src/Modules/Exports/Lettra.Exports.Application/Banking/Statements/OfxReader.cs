using System.Globalization;
using System.Text.RegularExpressions;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Banking.Statements;

/// <summary>
/// Relevé OFX : version 1.x (SGML, balises sans fermeture, ce que produisent les banques françaises) ou 2.x (XML). Les
/// opérations sont les blocs <c>STMTTRN</c> ; le montant <c>TRNAMT</c> est signé. Le compte est le numéro
/// <c>ACCTID</c> (rarement un IBAN).
/// </summary>
internal static partial class OfxReader
{
    public static Result<ParsedBankStatement> Read(string text)
    {
        int start = text.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return BankStatementErrors.FileInvalid(0);
        }

        string body = text[start..];
        string? account = Field(body, "ACCTID");
        DateOnly? from = Day(Field(body, "DTSTART"));
        DateOnly? to = Day(Field(body, "DTEND"));
        List<BankTransaction> transactions = [];
        foreach (Match block in TransactionBlock().Matches(body))
        {
            string content = block.Groups[1].Value;
            DateOnly? posted = Day(Field(content, "DTPOSTED")) ?? Day(Field(content, "DTUSER"));
            if (posted is null || !BankStatementParser.TryParseAmount(Field(content, "TRNAMT"), out decimal amount) || amount == 0m)
            {
                return BankStatementErrors.FileInvalid(LineOf(text, start + block.Index));
            }

            string? name = Field(content, "NAME");
            string? memo = Field(content, "MEMO");
            string label = string.Join(' ', new[] { name, memo }.Where(t => !string.IsNullOrWhiteSpace(t)));
            transactions.Add(new BankTransaction(
                posted.Value,
                amount,
                label.Length == 0 ? Field(content, "TRNTYPE") ?? "—" : label,
                Field(content, "REFNUM") ?? Field(content, "CHECKNUM"),
                name,
                null,
                Field(content, "FITID")));
        }

        return new ParsedBankStatement(BankStatementFormat.Ofx, account, from, to, transactions);
    }

    /// <summary>Valeur d'une balise : jusqu'à la balise suivante (SGML) ou à sa fermeture (XML), entités XML décodées.</summary>
    private static string? Field(string content, string tag)
    {
        Match match = Regex.Match(content, $"<{tag}>([^<\\r\\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            return null;
        }

        string value = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        return value.Length == 0 ? null : value;
    }

    private static DateOnly? Day(string? value) =>
        value is { Length: >= 8 } && DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;

    private static int LineOf(string text, int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

    /// <summary>Un bloc d'opération, fermé (<c>&lt;/STMTTRN&gt;</c>) ou non : il s'arrête au bloc suivant ou à la fin de la liste.</summary>
    [GeneratedRegex(@"<STMTTRN>(.*?)(?=</STMTTRN>|<STMTTRN>|</BANKTRANLIST>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex TransactionBlock();
}
