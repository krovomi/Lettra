using System.Globalization;
using System.Text;
using Lettra.Calculations;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Application.Bridges;

/// <summary>Un fichier d'écritures pour le logiciel de production du cabinet : son nom, son type et son contenu.</summary>
public sealed record BridgeFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Passerelles vers les logiciels de production des cabinets : les mêmes lignes que le FEC (<see cref="FecWriter.Lines"/>),
/// rien n'est recalculé, écrites dans le format d'import de chaque logiciel. Les choix à relire par un cabinet utilisateur
/// (jalon J2) sont regroupés ici : longueur des comptes généraux, forme des comptes auxiliaires, colonnes des imports
/// paramétrables de Sage et d'ACD.
/// </summary>
public static class BridgeWriters
{
    /// <summary>Longueur des comptes généraux dans Sage, Cegid et ACD : 706 devient 706000.</summary>
    public const int GeneralAccountLength = 6;

    /// <summary>Longueur des comptes dans Quadra : 706 devient 70600000, le client a son propre compte de 8 caractères.</summary>
    public const int QuadraAccountLength = 8;

    private const string Csv = "text/csv; charset=iso-8859-15";
    private const string CsvUtf8 = "text/csv; charset=utf-8";
    private const string Text = "text/plain; charset=iso-8859-15";

    /// <summary>Écrit le fichier du logiciel choisi. Le nom reprend le SIREN, le logiciel et la période.</summary>
    public static BridgeFile Write(AccountingSoftwareDto software, string siren, DateOnly from, DateOnly to, IEnumerable<SalesJournalExportEntry> entries)
    {
        IReadOnlyList<ExportLine> lines = [.. FecWriter.Lines(entries)];
        string stem = string.Create(CultureInfo.InvariantCulture, $"{siren}-{software.ToString().ToLowerInvariant()}-{from:yyyyMMdd}-{to:yyyyMMdd}");
        return software switch
        {
            AccountingSoftwareDto.Quadra => new(stem + ".txt", Text, FecFormat.Encoding.GetBytes(Quadra(lines))),
            AccountingSoftwareDto.Sage => new(stem + ".csv", Csv, FecFormat.Encoding.GetBytes(Sage(lines))),
            AccountingSoftwareDto.Cegid => new(stem + ".csv", CsvUtf8, new UTF8Encoding(false).GetBytes(Cegid(lines))),
            AccountingSoftwareDto.Acd => new(stem + ".csv", Csv, FecFormat.Encoding.GetBytes(Acd(lines))),
            _ => throw new ArgumentOutOfRangeException(nameof(software), software, null),
        };
    }

    /// <summary>
    /// Quadra : ASCII à positions fixes. Une ligne « C » par client (compte de 8 caractères rattaché au collectif 411),
    /// puis une ligne « M » par ligne d'écriture : compte, journal, date JJMMAA, sens, montant en centimes signé.
    /// </summary>
    public static string Quadra(IReadOnlyList<ExportLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        StringBuilder text = new();
        string collective = PadAccount(SalesAccounts.Customers, QuadraAccountLength);
        foreach (ExportLine customer in lines.Where(l => l.CustomerId is not null).DistinctBy(l => l.CustomerId))
        {
            FixedLine record = new(218);
            record.Put(1, 1, "C");
            record.Put(2, 8, QuadraAuxiliaryOf(customer.CustomerId!.Value));
            record.Put(10, 30, customer.AuxiliaryLabel);
            record.Put(99, 8, collective);
            record.Put(218, 1, "C");
            text.Append(record).Append(FecFormat.LineEnd);
        }

        foreach (ExportLine line in lines)
        {
            FixedLine record = new(251);
            record.Put(1, 1, "M");
            record.Put(2, 8, line.CustomerId is { } customer ? QuadraAuxiliaryOf(customer) : PadAccount(line.Account, QuadraAccountLength));
            record.Put(10, 2, line.Journal);
            record.Put(12, 3, "000");
            record.Put(15, 6, line.Date.ToString("ddMMyy", CultureInfo.InvariantCulture));
            record.Put(22, 20, line.Label);
            record.Put(42, 1, line.IsDebit ? "D" : "C");
            record.Put(43, 13, QuadraAmount(line.Amount));
            record.Put(100, 8, line.Piece);
            record.Put(108, 3, "EUR");
            record.Put(111, 3, line.Journal);
            record.Put(117, 30, line.Label);
            record.Put(149, 10, line.Piece);
            record.Put(232, 20, line.Piece);
            text.Append(record).Append(FecFormat.LineEnd);
        }

        return text.ToString();
    }

    /// <summary>Sage 100 : import paramétrable, une ligne d'en-tête, point-virgule, dates JJMMAA, virgule décimale.</summary>
    public static string Sage(IReadOnlyList<ExportLine> lines) =>
        Delimited(
            lines,
            ["Code journal", "Date", "N° compte général", "N° compte tiers", "N° pièce", "Libellé écriture", "Débit", "Crédit", "Lettrage"],
            l =>
            [
                l.Journal, l.Date.ToString("ddMMyy", CultureInfo.InvariantCulture), PadAccount(l.Account, GeneralAccountLength),
                l.AuxiliaryAccount, l.Piece, l.Label, CommaAmount(l.Debit), CommaAmount(l.Credit), l.LetteringCode,
            ]);

    /// <summary>
    /// Cegid Expert : modèle « écritures Excel », sans en-tête, point-virgule : journal, date JJ/MM/AAAA, compte général,
    /// compte auxiliaire, pièce, libellé (35 caractères), débit, crédit au point décimal. UTF-8.
    /// </summary>
    public static string Cegid(IReadOnlyList<ExportLine> lines) =>
        Delimited(
            lines,
            null,
            l =>
            [
                l.Journal, l.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), PadAccount(l.Account, GeneralAccountLength),
                l.AuxiliaryAccount, Cut(l.Piece, 35), Cut(l.Label, 35), PointAmount(l.Debit), PointAmount(l.Credit),
            ]);

    /// <summary>ACD : import paramétrable, une ligne d'en-tête, point-virgule, dates JJ/MM/AAAA, virgule décimale.</summary>
    public static string Acd(IReadOnlyList<ExportLine> lines) =>
        Delimited(
            lines,
            ["Journal", "Date", "Compte", "Compte auxiliaire", "Libellé auxiliaire", "Pièce", "Libellé", "Débit", "Crédit", "Lettrage"],
            l =>
            [
                l.Journal, l.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), PadAccount(l.Account, GeneralAccountLength),
                l.AuxiliaryAccount, l.AuxiliaryLabel, l.Piece, l.Label, CommaAmount(l.Debit), CommaAmount(l.Credit), l.LetteringCode,
            ]);

    /// <summary>Compte client dans Quadra : « C » suivi des sept derniers chiffres hexadécimaux de son identifiant.</summary>
    public static string QuadraAuxiliaryOf(Guid customerId) =>
        "C" + customerId.ToString("N").ToUpperInvariant()[^7..];

    /// <summary>Complète un compte général de zéros à droite : 44571 devient 445710 sur six caractères.</summary>
    public static string PadAccount(string account, int length)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Length >= length ? account : account.PadRight(length, '0');
    }

    /// <summary>Montant Quadra : signe puis douze chiffres, en centimes.</summary>
    public static string QuadraAmount(decimal amount)
    {
        long cents = (long)decimal.Round(amount * 100m, MidpointRounding.AwayFromZero);
        return (cents < 0 ? "-" : "+") + Math.Abs(cents).ToString("D12", CultureInfo.InvariantCulture);
    }

    private static string CommaAmount(decimal amount) => amount == 0m ? string.Empty : FecFormat.FormatAmount(amount);

    private static string PointAmount(decimal amount) =>
        amount == 0m ? string.Empty : amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];

    private static string Delimited(IReadOnlyList<ExportLine> lines, IReadOnlyList<string>? header, Func<ExportLine, IReadOnlyList<string?>> fields)
    {
        ArgumentNullException.ThrowIfNull(lines);
        StringBuilder text = new();
        if (header is not null)
        {
            text.AppendJoin(';', header).Append(FecFormat.LineEnd);
        }

        foreach (ExportLine line in lines)
        {
            // Les libellés sont déjà nettoyés (FecFormat.Clean) : seul le point-virgule reste à écarter.
            text.AppendJoin(';', fields(line).Select(f => (f ?? string.Empty).Replace(';', ','))).Append(FecFormat.LineEnd);
        }

        return text.ToString();
    }

    /// <summary>Une ligne à positions fixes (numérotées à partir de 1), remplie d'espaces.</summary>
    private sealed class FixedLine(int length)
    {
        private readonly char[] _chars = [.. Enumerable.Repeat(' ', length)];

        public void Put(int position, int width, string? value)
        {
            string text = value ?? string.Empty;
            text = text.Length > width ? text[..width] : text;
            text.CopyTo(0, _chars, position - 1, text.Length);
        }

        public override string ToString() => new(_chars);
    }
}
