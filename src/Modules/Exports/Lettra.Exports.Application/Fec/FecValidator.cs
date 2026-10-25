using System.Text.RegularExpressions;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;

namespace Lettra.Exports.Application.Fec;

/// <summary>
/// Validateur intégré du FEC, sur le modèle des contrôles de l'outil Test Compta Demat de la DGFiP : nom du fichier,
/// en-tête et nombre de colonnes, champs obligatoires, dates et montants, comptes, couples de colonnes (auxiliaire,
/// lettrage, devise), équilibre de chaque écriture et du fichier, ordre chronologique de validation, et, si la période
/// est donnée, dates dans l'exercice. Il lit n'importe quel FEC (celui de Lettra ou celui d'un autre logiciel).
/// </summary>
public static partial class FecValidator
{
    public const int MaxFindings = 200;

    public static FecValidationResponse Validate(string fileName, ReadOnlySpan<byte> content, DateOnly? periodStart = null, DateOnly? periodEnd = null)
    {
        Report report = new();
        ValidateFileName(fileName, periodEnd, report);

        List<string> lines = ReadLines(content);
        if (lines.Count == 0)
        {
            report.Add(ErrorCodes.Fec.Empty, FecFindingSeverityDto.Error);
            return report.ToResponse(fileName, 0, 0);
        }

        if (ReadSeparator(lines[0], report) is not { } separator)
        {
            return report.ToResponse(fileName, lines.Count, 0);
        }

        if (lines.Count == 1)
        {
            report.Add(ErrorCodes.Fec.NoEntries, FecFindingSeverityDto.Warning);
        }

        Ledger ledger = new(report);
        for (int index = 1; index < lines.Count; index++)
        {
            int lineNumber = index + 1;
            string[] fields = lines[index].Split(separator);
            if (fields.Length != FecFormat.Columns.Count)
            {
                report.Add(ErrorCodes.Fec.ColumnCountInvalid, FecFindingSeverityDto.Error, lineNumber);
                continue;
            }

            Row row = new(fields);
            ValidateRow(row, lineNumber, periodStart, periodEnd, report);
            ledger.Post(row, lineNumber);
        }

        ledger.CheckBalances();
        return report.ToResponse(fileName, lines.Count, ledger.EntryCount);
    }

    /// <summary>Les lignes du fichier, sans fin de ligne ni dernière ligne vide.</summary>
    private static List<string> ReadLines(ReadOnlySpan<byte> content)
    {
        List<string> lines = [.. FecFormat.Decode(content).Split('\n').Select(l => l.TrimEnd('\r'))];
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>Le séparateur (tabulation ou barre verticale) d'un en-tête conforme, ou rien s'il ne l'est pas.</summary>
    private static char? ReadSeparator(string headerLine, Report report)
    {
        char? separator = headerLine.Contains('\t', StringComparison.Ordinal) ? '\t'
            : headerLine.Contains('|', StringComparison.Ordinal) ? '|'
            : null;
        if (separator is null)
        {
            report.Add(ErrorCodes.Fec.SeparatorInvalid, FecFindingSeverityDto.Error, line: 1);
            return null;
        }

        string[] header = headerLine.TrimStart('\uFEFF').Split(separator.Value);
        if (!header.SequenceEqual(FecFormat.Columns, StringComparer.OrdinalIgnoreCase))
        {
            report.Add(ErrorCodes.Fec.HeaderInvalid, FecFindingSeverityDto.Error, line: 1);
            return null;
        }

        return separator;
    }

    private static void ValidateFileName(string fileName, DateOnly? periodEnd, Report report)
    {
        Match match = FileNamePattern().Match(fileName ?? string.Empty);
        if (!match.Success || !FecFormat.TryParseDate(match.Groups["date"].Value, out DateOnly closing))
        {
            report.Add(ErrorCodes.Fec.FileNameInvalid, FecFindingSeverityDto.Error);
        }
        else if (periodEnd is { } end && closing != end)
        {
            // Le nom porte la date de clôture de l'exercice.
            report.Add(ErrorCodes.Fec.FileNameInvalid, FecFindingSeverityDto.Warning);
        }
    }

    private static void ValidateRow(Row row, int line, DateOnly? periodStart, DateOnly? periodEnd, Report report)
    {
        string number = row[FecFormat.EcritureNum];
        foreach (string column in FecFormat.Columns.Where(FecFormat.Required.Contains).Where(c => row[c].Length == 0))
        {
            report.Add(ErrorCodes.Fec.FieldRequired, FecFindingSeverityDto.Error, line, column, number);
        }

        DateOnly entryDate = default;
        foreach (string column in new[] { FecFormat.EcritureDate, FecFormat.PieceDate, FecFormat.ValidDate, FecFormat.DateLet })
        {
            string value = row[column];
            if (value.Length == 0)
            {
                continue;
            }

            if (!FecFormat.TryParseDate(value, out DateOnly date))
            {
                report.Add(ErrorCodes.Fec.DateInvalid, FecFindingSeverityDto.Error, line, column, number);
            }
            else if (column == FecFormat.EcritureDate)
            {
                entryDate = date;
                if ((periodStart is { } start && date < start) || (periodEnd is { } end && date > end))
                {
                    report.Add(ErrorCodes.Fec.DateOutsidePeriod, FecFindingSeverityDto.Error, line, column, number);
                }
            }
            else if (column == FecFormat.ValidDate && entryDate != default && date < entryDate)
            {
                report.Add(ErrorCodes.Fec.ValidationBeforeEntry, FecFindingSeverityDto.Warning, line, column, number);
            }
        }

        decimal? debit = Amount(row, FecFormat.Debit, line, number, report);
        decimal? credit = Amount(row, FecFormat.Credit, line, number, report);
        Amount(row, FecFormat.Montantdevise, line, number, report);
        if (debit is { } d && credit is { } c)
        {
            if (d != 0m && c != 0m)
            {
                report.Add(ErrorCodes.Fec.DebitAndCredit, FecFindingSeverityDto.Error, line, FecFormat.Debit, number);
            }
            else if (d == 0m && c == 0m)
            {
                report.Add(ErrorCodes.Fec.ZeroLine, FecFindingSeverityDto.Warning, line, FecFormat.Debit, number);
            }
        }

        string account = row[FecFormat.CompteNum];
        if (account.Length > 0 && !AccountPattern().IsMatch(account))
        {
            report.Add(ErrorCodes.Fec.AccountInvalid, FecFindingSeverityDto.Error, line, FecFormat.CompteNum, number);
        }

        Pair(row, FecFormat.CompAuxNum, FecFormat.CompAuxLib, ErrorCodes.Fec.AuxiliaryIncomplete, line, number, report);
        Pair(row, FecFormat.EcritureLet, FecFormat.DateLet, ErrorCodes.Fec.LetteringIncomplete, line, number, report);
        Pair(row, FecFormat.Montantdevise, FecFormat.Idevise, ErrorCodes.Fec.CurrencyIncomplete, line, number, report);
    }

    private static decimal? Amount(Row row, string column, int line, string number, Report report)
    {
        string value = row[column];
        if (value.Length == 0)
        {
            return null;
        }

        if (!FecFormat.TryParseAmount(value, out decimal amount))
        {
            report.Add(ErrorCodes.Fec.AmountInvalid, FecFindingSeverityDto.Error, line, column, number);
            return null;
        }

        if (amount < 0m && column != FecFormat.Montantdevise)
        {
            // Un montant négatif est recevable, mais un débit négatif se passe normalement au crédit.
            report.Add(ErrorCodes.Fec.AmountInvalid, FecFindingSeverityDto.Warning, line, column, number);
        }

        return amount;
    }

    private static void Pair(Row row, string first, string second, string code, int line, string number, Report report)
    {
        if ((row[first].Length == 0) != (row[second].Length == 0))
        {
            report.Add(code, FecFindingSeverityDto.Error, line, row[first].Length == 0 ? first : second, number);
        }
    }

    [GeneratedRegex(@"^(?<siren>\d{9})FEC(?<date>\d{8})(\.txt|\.xml)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    /// <summary>Un compte du plan comptable : classe 1 à 8, au moins trois caractères.</summary>
    [GeneratedRegex(@"^[1-8][0-9A-Za-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();

    private sealed class Row(string[] fields)
    {
        private static readonly Dictionary<string, int> Index =
            FecFormat.Columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i, StringComparer.Ordinal);

        public string this[string column] => fields[Index[column]].Trim();
    }

    private sealed class Entry(string journal, string number, string entryDate, string validDate)
    {
        public string Journal { get; } = journal;

        public string Number { get; } = number;

        public string EntryDate { get; } = entryDate;

        public string ValidDate { get; } = validDate;

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }
    }

    /// <summary>Les écritures du fichier : regroupement des lignes, ordre de validation par journal, équilibres.</summary>
    private sealed class Ledger(Report report)
    {
        private readonly Dictionary<(string Journal, string Number), Entry> _entries = [];
        private readonly Dictionary<string, DateOnly> _lastValidation = new(StringComparer.Ordinal);

        public int EntryCount => _entries.Count;

        public void Post(Row row, int lineNumber)
        {
            string journal = row[FecFormat.JournalCode];
            string number = row[FecFormat.EcritureNum];
            if (journal.Length == 0 || number.Length == 0)
            {
                return;
            }

            if (!_entries.TryGetValue((journal, number), out Entry? entry))
            {
                entry = new Entry(journal, number, row[FecFormat.EcritureDate], row[FecFormat.ValidDate]);
                _entries.Add((journal, number), entry);
                CheckChronology(entry, lineNumber);
            }
            else if (entry.EntryDate != row[FecFormat.EcritureDate] || entry.ValidDate != row[FecFormat.ValidDate])
            {
                report.Add(ErrorCodes.Fec.EntryInconsistent, FecFindingSeverityDto.Error, lineNumber, FecFormat.EcritureDate, number);
            }

            if (FecFormat.TryParseAmount(row[FecFormat.Debit], out decimal debit) && FecFormat.TryParseAmount(row[FecFormat.Credit], out decimal credit))
            {
                entry.Debit += debit;
                entry.Credit += credit;
                report.TotalDebit += debit;
                report.TotalCredit += credit;
            }
        }

        public void CheckBalances()
        {
            foreach (Entry entry in _entries.Values.Where(e => e.Debit != e.Credit))
            {
                report.Add(ErrorCodes.Fec.EntryUnbalanced, FecFindingSeverityDto.Error, entryNumber: entry.Number);
            }

            if (report.TotalDebit != report.TotalCredit)
            {
                report.Add(ErrorCodes.Fec.FileUnbalanced, FecFindingSeverityDto.Error);
            }
        }

        /// <summary>Les écritures d'un journal se suivent dans l'ordre de leur validation.</summary>
        private void CheckChronology(Entry entry, int lineNumber)
        {
            if (!FecFormat.TryParseDate(entry.ValidDate, out DateOnly validated))
            {
                return;
            }

            if (_lastValidation.TryGetValue(entry.Journal, out DateOnly previous) && validated < previous)
            {
                report.Add(ErrorCodes.Fec.NotChronological, FecFindingSeverityDto.Error, lineNumber, FecFormat.ValidDate, entry.Number);
            }

            _lastValidation[entry.Journal] = validated;
        }
    }

    private sealed class Report
    {
        private readonly List<FecFindingResponse> _findings = [];

        public decimal TotalDebit { get; set; }

        public decimal TotalCredit { get; set; }

        public bool HasErrors { get; private set; }

        public bool Truncated { get; private set; }

        public void Add(string code, FecFindingSeverityDto severity, int? line = null, string? column = null, string? entryNumber = null)
        {
            HasErrors |= severity == FecFindingSeverityDto.Error;
            if (_findings.Count >= MaxFindings)
            {
                Truncated = true;
                return;
            }

            _findings.Add(new FecFindingResponse(code, severity, line, column, string.IsNullOrEmpty(entryNumber) ? null : entryNumber));
        }

        public FecValidationResponse ToResponse(string fileName, int lineCount, int entryCount) =>
            new(!HasErrors, fileName, lineCount, entryCount, TotalDebit, TotalCredit, _findings, Truncated);
    }
}
