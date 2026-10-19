using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// Démo : un relevé ISO 20022 <c>camt.053.001.08</c> fictif, fabriqué à partir des factures dues du dossier, qui montre
/// chaque règle de rapprochement : numéro de facture dans le libellé, montant et nom du client, règlement partiel,
/// règlement amputé de frais (écart de règlement), plusieurs factures payées d'un coup. S'y ajoutent des frais bancaires
/// et une remise de chèque sans facture, à écarter ou imputer à la main. Comptes et IBAN fictifs.
/// </summary>
public static class DemoBankStatement
{
    /// <summary>IBAN fictif du compte du dossier (format français, ne désigne aucun compte réel).</summary>
    public const string AccountIban = "FR7610107001011234567890129";

    private const string PayerIban = "FR7630004000031234567890143";

    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";

    public static string FileName(DateOnly today) => $"releve-demo-{today:yyyy-MM-dd}.xml";

    public static byte[] Build(IReadOnlyList<ReceivableDocument> documents, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(documents);
        List<ReceivableDocument> open =
        [
            .. documents.Where(d => d.Kind != InvoiceKind.CreditNote && d.Outstanding > 0m)
                .OrderBy(d => d.DueDate ?? d.IssueDate).ThenBy(d => d.Number, StringComparer.Ordinal),
        ];
        List<(DateOnly Date, decimal Amount, string Label, string? Name, string? Iban)> entries = [];
        HashSet<ReceivableDocument> used = [];
        DateOnly DateFor(ReceivableDocument d, int daysAgo)
        {
            DateOnly date = today.AddDays(-daysAgo);
            return date < d.IssueDate ? d.IssueDate : date;
        }

        string Upper(string name) => name.ToUpperInvariant();

        // 1. Virement qui cite le numéro de la facture, avec l'IBAN du payeur.
        if (open.ElementAtOrDefault(0) is { } first)
        {
            used.Add(first);
            entries.Add((DateFor(first, 6), first.Outstanding, $"VIR SEPA {Upper(first.CustomerName)} REF FACTURE {first.Number}", first.CustomerName, PayerIban));
        }

        // 2. Virement du montant exact, au nom du client, sans numéro.
        if (open.Skip(1).FirstOrDefault(d => !used.Contains(d)) is { } second)
        {
            used.Add(second);
            entries.Add((DateFor(second, 5), second.Outstanding, $"VIR {Upper(second.CustomerName)} REGLEMENT", second.CustomerName, null));
        }

        // 3. Règlement partiel : la moitié, numéro cité.
        if (open.FirstOrDefault(d => !used.Contains(d) && d.Outstanding >= 2m) is { } partial)
        {
            used.Add(partial);
            decimal half = decimal.Round(partial.Outstanding / 2m, 2, MidpointRounding.ToZero);
            entries.Add((DateFor(partial, 4), half, $"VIR ACOMPTE {partial.Number} {Upper(partial.CustomerName)}", partial.CustomerName, null));
        }

        // 4. Règlement amputé de 0,40 € de frais : écart de règlement sous le seuil.
        if (open.FirstOrDefault(d => !used.Contains(d) && d.Outstanding > 10m) is { } shortPaid)
        {
            used.Add(shortPaid);
            entries.Add((DateFor(shortPaid, 3), shortPaid.Outstanding - 0.40m, $"VIR INTL {shortPaid.Number} FRAIS DEDUITS", shortPaid.CustomerName, null));
        }

        // 5. Deux factures d'un même client payées par un seul virement, sans numéro.
        IGrouping<Guid, ReceivableDocument>? pair = open.Where(d => !used.Contains(d)).GroupBy(d => d.CustomerId.Value).FirstOrDefault(g => g.Count() >= 2);
        if (pair is not null)
        {
            ReceivableDocument[] two = [.. pair.Take(2)];
            used.UnionWith(two);
            entries.Add((DateFor(two[1], 2), two[0].Outstanding + two[1].Outstanding, $"VIREMENT {Upper(two[0].CustomerName)} FACTURES", two[0].CustomerName, null));
        }

        // 6. Ni facture ni client : frais bancaires (débit) et remise de chèque.
        entries.Add((today.AddDays(-2), -12.50m, "FRAIS TENUE DE COMPTE", null, null));
        entries.Add((today.AddDays(-1), 250.00m, "REMISE CHEQUE 4521873", null, null));

        DateOnly from = entries.Min(e => e.Date);
        XElement statement = new(
            Ns + "Stmt",
            new XElement(Ns + "Id", $"DEMO-{today:yyyyMMdd}"),
            new XElement(Ns + "CreDtTm", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T07:00:00"),
            new XElement(Ns + "FrToDt",
                new XElement(Ns + "FrDtTm", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00"),
                new XElement(Ns + "ToDtTm", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59")),
            new XElement(Ns + "Acct",
                new XElement(Ns + "Id", new XElement(Ns + "IBAN", AccountIban)),
                new XElement(Ns + "Ccy", "EUR")),
            entries.OrderBy(e => e.Date).Select((e, i) => Entry(e.Date, e.Amount, e.Label, e.Name, e.Iban, i + 1, today)));
        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XElement(Ns + "BkToCstmrStmt",
                    new XElement(Ns + "GrpHdr",
                        new XElement(Ns + "MsgId", $"LETTRA-DEMO-{today:yyyyMMdd}"),
                        new XElement(Ns + "CreDtTm", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T07:00:00")),
                    statement)));
        using MemoryStream stream = new();
        using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static XElement Entry(DateOnly date, decimal amount, string label, string? name, string? iban, int rank, DateOnly today)
    {
        bool credit = amount > 0m;
        string day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string value = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
        string side = credit ? "Dbtr" : "Cdtr";
        XElement? parties = name is null
            ? null
            : new XElement(Ns + "RltdPties",
                new XElement(Ns + side, new XElement(Ns + "Pty", new XElement(Ns + "Nm", name))),
                iban is null ? null : new XElement(Ns + side + "Acct", new XElement(Ns + "Id", new XElement(Ns + "IBAN", iban))));
        return new XElement(
            Ns + "Ntry",
            new XElement(Ns + "Amt", new XAttribute("Ccy", "EUR"), value),
            new XElement(Ns + "CdtDbtInd", credit ? "CRDT" : "DBIT"),
            new XElement(Ns + "Sts", new XElement(Ns + "Cd", "BOOK")),
            new XElement(Ns + "BookgDt", new XElement(Ns + "Dt", day)),
            new XElement(Ns + "ValDt", new XElement(Ns + "Dt", day)),
            new XElement(Ns + "AcctSvcrRef", $"DEMO{today:yyyyMMdd}{rank:D4}"),
            new XElement(Ns + "NtryDtls",
                new XElement(Ns + "TxDtls",
                    new XElement(Ns + "Refs", new XElement(Ns + "EndToEndId", "NOTPROVIDED")),
                    parties,
                    new XElement(Ns + "RmtInf", new XElement(Ns + "Ustrd", label)))));
    }
}
