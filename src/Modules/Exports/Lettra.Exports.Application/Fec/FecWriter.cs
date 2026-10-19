using System.Text;
using Lettra.Calculations;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Fec;

/// <summary>
/// Écrit le FEC des journaux des ventes (VE) et de banque (BQ, vague 6) depuis les écritures enregistrées, sans rien
/// recalculer : une écriture par pièce ou par encaissement, numérotée en séquence continue par journal dans l'ordre
/// d'enregistrement ; le client (411) porte le compte auxiliaire du client et, s'il est lettré, le code et la date de
/// lettrage. Les choix à relire par l'expert-comptable (jalon J2) sont regroupés ici : codes et libellés des journaux,
/// libellés des comptes, forme du compte auxiliaire.
/// </summary>
public static class FecWriter
{
    public const string SalesJournalCode = "VE";
    public const string SalesJournalLabel = "Journal des ventes";
    public const string BankJournalCode = PaymentAccounts.JournalCode;
    public const string BankJournalLabel = "Journal de banque";

    /// <summary>Libellés des comptes du plan comptable général utilisés par les écritures de vente.</summary>
    public static readonly IReadOnlyDictionary<string, string> AccountLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SalesAccounts.Customers] = "Clients",
        [SalesAccounts.ServicesRevenue] = "Prestations de services",
        [SalesAccounts.GoodsRevenue] = "Ventes de marchandises",
        [SalesAccounts.DownPaymentsReceived] = "Clients - Avances et acomptes reçus sur commandes",
        [SalesAccounts.VatCollected] = "TVA collectée",
        [SalesAccounts.VatPending] = "TVA collectée sur encaissements en attente",
        [PaymentAccounts.Bank] = "Banque",
        [PaymentAccounts.WriteOffLoss] = "Charges diverses de gestion courante",
        [PaymentAccounts.WriteOffGain] = "Produits divers de gestion courante",
    };

    /// <summary>
    /// Compte auxiliaire du client : « C » suivi des dix derniers chiffres hexadécimaux de son identifiant (la partie
    /// aléatoire d'un UUID v7), stable et unique dans le dossier. Un cabinet pourra le remplacer par sa codification.
    /// </summary>
    public static string AuxiliaryAccountOf(Guid customerId) =>
        "C" + customerId.ToString("N").ToUpperInvariant()[^10..];

    public static string EntryLabel(InvoiceKind kind, string pieceNumber, string customerName) =>
        FecFormat.Clean($"{KindLabel(kind)} {pieceNumber} {customerName}");

    public static string EntryLabel(SalesJournalExportEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsBankEntry
            ? FecFormat.Clean($"{(entry.IsReversal ? "Contre-passation encaissement" : "Encaissement")} {entry.PieceNumber} {entry.CustomerName}")
            : EntryLabel(entry.Kind, entry.PieceNumber, entry.CustomerName);
    }

    /// <summary>Le fichier complet (en-tête compris), encodé en ISO 8859-15.</summary>
    public static byte[] Write(IEnumerable<SalesJournalExportEntry> entries) =>
        FecFormat.Encoding.GetBytes(WriteText(entries));

    public static string WriteText(IEnumerable<SalesJournalExportEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        StringBuilder text = new();
        AppendLine(text, FecFormat.Columns);

        Dictionary<string, int> numbers = new(StringComparer.Ordinal);
        foreach (SalesJournalExportEntry entry in entries)
        {
            string journal = entry.IsBankEntry ? BankJournalCode : SalesJournalCode;
            int number = numbers.GetValueOrDefault(journal) + 1;
            numbers[journal] = number;
            string entryNumber = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string date = FecFormat.FormatDate(entry.Date);
            string label = EntryLabel(entry);
            foreach (AccountingEntryLine line in entry.Lines)
            {
                bool customer = line.Account == SalesAccounts.Customers;
                bool lettered = customer && entry.LetteringCode is not null && entry.LetteringDate is not null;
                AppendLine(text,
                [
                    journal,
                    entry.IsBankEntry ? BankJournalLabel : SalesJournalLabel,
                    entryNumber,
                    date,
                    line.Account,
                    AccountLabels.TryGetValue(line.Account, out string? accountLabel) ? accountLabel : line.Account,
                    customer ? AuxiliaryAccountOf(entry.CustomerId) : string.Empty,
                    customer ? FecFormat.Clean(entry.CustomerName) : string.Empty,
                    FecFormat.Clean(entry.PieceNumber),
                    date,
                    label,
                    FecFormat.FormatAmount(line.Debit),
                    FecFormat.FormatAmount(line.Credit),
                    lettered ? entry.LetteringCode! : string.Empty,
                    lettered ? FecFormat.FormatDate(entry.LetteringDate!.Value) : string.Empty,
                    // La pièce est validée le jour de sa date (la validation la date du jour, heure de Paris).
                    date,
                    string.Empty,
                    string.Empty,
                ]);
            }
        }

        return text.ToString();
    }

    private static string KindLabel(InvoiceKind kind) => kind switch
    {
        InvoiceKind.DownPayment => "Facture d'acompte",
        InvoiceKind.CreditNote => "Avoir",
        _ => "Facture",
    };

    private static void AppendLine(StringBuilder text, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                text.Append(FecFormat.Separator);
            }

            text.Append(fields[i]);
        }

        text.Append(FecFormat.LineEnd);
    }
}
