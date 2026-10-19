using System.Globalization;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Banking.Statements;

/// <summary>
/// Relevé CSV, paramétrable : séparateur (deviné sur la première ligne s'il n'est pas donné), colonnes numérotées à
/// partir de 1, format de date. Sans réglage des colonnes, l'en-tête est reconnu en français ou en anglais (Date,
/// Libellé, Montant, Débit, Crédit, Référence, Tiers…). Un montant unique est signé ; avec Débit et Crédit, le débit
/// (positif ou négatif dans le fichier) devient une sortie.
/// </summary>
internal static class CsvStatementReader
{
    private static readonly string[] DateHeaders = ["DATE OPERATION", "DATE COMPTABLE", "DATE DE COMPTABILISATION", "BOOKING DATE", "DATE"];
    private static readonly string[] LabelHeaders = ["LIBELLE", "LIBELLE OPERATION", "LIBELLE DE L OPERATION", "INTITULE", "DESCRIPTION", "LABEL", "DETAIL"];
    private static readonly string[] AmountHeaders = ["MONTANT", "MONTANT EUR", "MONTANT EN EUROS", "MONTANT EUROS", "AMOUNT"];
    private static readonly string[] DebitHeaders = ["DEBIT", "DEBIT EUR", "DEBIT EUROS", "DEBIT EN EUROS"];
    private static readonly string[] CreditHeaders = ["CREDIT", "CREDIT EUR", "CREDIT EUROS", "CREDIT EN EUROS"];
    private static readonly string[] ReferenceHeaders = ["REFERENCE", "REF", "REFERENCE OPERATION"];
    private static readonly string[] CounterpartyHeaders = ["TIERS", "CONTREPARTIE", "BENEFICIAIRE", "EMETTEUR", "PAYEUR", "NOM", "COUNTERPARTY", "PAYER"];

    private sealed record Columns(int Date, int Label, int? Amount, int? Debit, int? Credit, int? Reference, int? Counterparty, string? DateFormat);

    public static Result<ParsedBankStatement> Read(string text, CsvImportOptions? options)
    {
        CsvImportOptions settings = options ?? new CsvImportOptions();
        List<string> lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0)];
        if (lines.Count == 0)
        {
            return BankStatementErrors.NoTransactions;
        }

        char separator = settings.Separator is { Length: 1 } given ? given[0] : Guess(lines[0]);
        if (separator is not (';' or ',' or '\t' or '|'))
        {
            return BankStatementErrors.CsvColumnsInvalid;
        }

        List<string> header = Split(lines[0], separator);
        Columns? columns = Resolve(settings, settings.HasHeader ? header : null);
        if (columns is null)
        {
            return BankStatementErrors.CsvColumnsInvalid;
        }

        List<BankTransaction> transactions = [];
        for (int i = settings.HasHeader ? 1 : 0; i < lines.Count; i++)
        {
            List<string> cells = Split(lines[i], separator);
            string? Cell(int? column) => column is int c && c >= 1 && c <= cells.Count ? cells[c - 1].Trim() : null;

            if (!BankStatementParser.TryParseDate(Cell(columns.Date), columns.DateFormat, out DateOnly date))
            {
                // Lignes de pied (solde, totaux) sans date : on les saute ; une date illisible ailleurs est une erreur.
                if (string.IsNullOrWhiteSpace(Cell(columns.Date)) || !char.IsAsciiDigit(Cell(columns.Date)![0]))
                {
                    continue;
                }

                return BankStatementErrors.FileInvalid(i + 1);
            }

            decimal amount;
            if (columns.Amount is not null)
            {
                if (!BankStatementParser.TryParseAmount(Cell(columns.Amount), out amount))
                {
                    return BankStatementErrors.FileInvalid(i + 1);
                }
            }
            else
            {
                bool hasDebit = BankStatementParser.TryParseAmount(Cell(columns.Debit), out decimal debit);
                bool hasCredit = BankStatementParser.TryParseAmount(Cell(columns.Credit), out decimal credit);
                if (!hasDebit && !hasCredit)
                {
                    return BankStatementErrors.FileInvalid(i + 1);
                }

                amount = (hasCredit ? Math.Abs(credit) : 0m) - (hasDebit ? Math.Abs(debit) : 0m);
            }

            if (amount == 0m)
            {
                continue;
            }

            string label = Cell(columns.Label) ?? string.Empty;
            transactions.Add(new BankTransaction(
                date,
                amount,
                label.Length == 0 ? "—" : label,
                Cell(columns.Reference) is { Length: > 0 } reference ? reference : null,
                Cell(columns.Counterparty) is { Length: > 0 } name ? name : null,
                null,
                null));
        }

        return new ParsedBankStatement(BankStatementFormat.Csv, null, null, null, transactions);
    }

    private static Columns? Resolve(CsvImportOptions settings, List<string>? header)
    {
        List<string> folded = header?.Select(Fold).ToList() ?? [];
        int? Find(int? given, string[] names)
        {
            if (given is not null)
            {
                return given;
            }

            foreach (string name in names)
            {
                int index = folded.IndexOf(name);
                if (index >= 0)
                {
                    return index + 1;
                }
            }

            return null;
        }

        int? date = Find(settings.DateColumn, DateHeaders);
        int? label = Find(settings.LabelColumn, LabelHeaders);
        int? amount = Find(settings.AmountColumn, AmountHeaders);
        int? debit = Find(settings.DebitColumn, DebitHeaders);
        int? credit = Find(settings.CreditColumn, CreditHeaders);
        if (settings.DebitColumn is not null || settings.CreditColumn is not null)
        {
            amount = settings.AmountColumn;
        }

        bool valid = date is >= 1 && label is >= 1 && (amount is >= 1 || debit is >= 1 || credit is >= 1)
            && new[] { date, label, amount, debit, credit, settings.ReferenceColumn, settings.CounterpartyColumn }.All(c => c is null or (>= 1 and <= 200));
        return valid
            ? new Columns(date!.Value, label!.Value, amount, amount is null ? debit : null, amount is null ? credit : null,
                Find(settings.ReferenceColumn, ReferenceHeaders), Find(settings.CounterpartyColumn, CounterpartyHeaders), settings.DateFormat)
            : null;
    }

    private static char Guess(string firstLine)
    {
        char[] candidates = [';', '\t', ',', '|'];
        return candidates.OrderByDescending(c => firstLine.Count(x => x == c)).First();
    }

    /// <summary>Découpe une ligne CSV ; les champs entre guillemets peuvent contenir le séparateur et des guillemets doublés.</summary>
    internal static List<string> Split(string line, char separator)
    {
        List<string> cells = [];
        StringBuilder current = new();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }

    private static string Fold(string text)
    {
        string decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder folded = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            folded.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        }

        return string.Join(' ', folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
