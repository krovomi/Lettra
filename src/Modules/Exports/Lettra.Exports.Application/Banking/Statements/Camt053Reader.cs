using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.Banking.Statements;

/// <summary>
/// Relevé ISO 20022 <c>camt.053</c> (versions 001.02 à 001.13 : les éléments sont lus par leur nom local, quel que soit
/// l'espace de noms). Seules les écritures comptabilisées (<c>BOOK</c>) sont retenues. Une écriture qui regroupe
/// plusieurs opérations détaillées, chacune avec son montant, donne une opération par détail. Pour un crédit, la
/// contrepartie est le débiteur ; pour un débit, le créancier. DTD et entités externes refusées.
/// </summary>
internal static class Camt053Reader
{
    public static Result<ParsedBankStatement> Read(string xml)
    {
        XDocument document;
        try
        {
            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return BankStatementErrors.FileInvalid(ex.LineNumber);
        }

        List<XElement> statements = [.. document.Descendants().Where(e => e.Name.LocalName == "Stmt")];
        if (statements.Count == 0)
        {
            return BankStatementErrors.FileInvalid(0);
        }

        List<BankTransaction> transactions = [];
        string? account = null;
        DateOnly? from = null;
        DateOnly? to = null;
        foreach (XElement statement in statements)
        {
            account ??= Value(Child(Child(Child(statement, "Acct"), "Id"), "IBAN")) ?? Value(Child(Child(Child(Child(statement, "Acct"), "Id"), "Othr"), "Id"));
            XElement? period = Child(statement, "FrToDt");
            if (Day(Value(Child(period, "FrDtTm")) ?? Value(Child(period, "FrDt"))) is { } start && (from is null || start < from))
            {
                from = start;
            }

            if (Day(Value(Child(period, "ToDtTm")) ?? Value(Child(period, "ToDt"))) is { } end && (to is null || end > to))
            {
                to = end;
            }

            foreach (XElement entry in Children(statement, "Ntry"))
            {
                Result<IReadOnlyList<BankTransaction>> read = ReadEntry(entry);
                if (read.IsFailure)
                {
                    return Result<ParsedBankStatement>.Failure(read.Errors);
                }

                transactions.AddRange(read.Value);
            }
        }

        return new ParsedBankStatement(BankStatementFormat.Camt053, account, from, to, transactions);
    }

    private static Result<IReadOnlyList<BankTransaction>> ReadEntry(XElement entry)
    {
        XElement? status = Child(entry, "Sts");
        string? code = Value(Child(status, "Cd")) ?? Value(status);
        if (code is not null && !string.Equals(code, "BOOK", StringComparison.OrdinalIgnoreCase))
        {
            return Result<IReadOnlyList<BankTransaction>>.Success([]);
        }

        bool credit = string.Equals(Value(Child(entry, "CdtDbtInd")), "CRDT", StringComparison.OrdinalIgnoreCase);
        DateOnly? booked = Day(Value(Child(Child(entry, "BookgDt"), "Dt")) ?? Value(Child(Child(entry, "BookgDt"), "DtTm")))
            ?? Day(Value(Child(Child(entry, "ValDt"), "Dt")) ?? Value(Child(Child(entry, "ValDt"), "DtTm")));
        if (booked is null || !TryAmount(Child(entry, "Amt"), out decimal entryAmount))
        {
            return BankStatementErrors.FileInvalid(LineOf(entry));
        }

        string? bankReference = Value(Child(entry, "AcctSvcrRef"));
        string? additional = Value(Child(entry, "AddtlNtryInf"));
        List<XElement> details = [.. Children(Child(entry, "NtryDtls"), "TxDtls")];
        bool split = details.Count > 1 && details.All(d => AmountOf(d) is not null);
        List<BankTransaction> transactions = [];
        List<XElement?> parts = split ? [.. details] : [details.FirstOrDefault()];
        foreach (XElement? detail in parts)
        {
            decimal amount = split ? AmountOf(detail!)!.Value : entryAmount;
            string? remittance = detail is null ? null : string.Join(' ', Children(Child(detail, "RmtInf"), "Ustrd").Select(u => u.Value.Trim()));
            string? structured = detail is null ? null : Value(Child(Child(Child(Child(detail, "RmtInf"), "Strd"), "CdtrRefInf"), "Ref"));
            string? endToEnd = detail is null ? null : Value(Child(Child(detail, "Refs"), "EndToEndId"));
            XElement? parties = Child(detail, "RltdPties");
            string side = credit ? "Dbtr" : "Cdtr";
            XElement? party = Child(parties, side);
            string? name = Value(Child(party, "Nm")) ?? Value(Child(Child(party, "Pty"), "Nm"));
            string? iban = Value(Child(Child(Child(parties, side + "Acct"), "Id"), "IBAN"));
            string label = string.Join(' ', new[] { remittance, additional }.Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
            if (label.Length == 0)
            {
                label = name ?? bankReference ?? "—";
            }

            transactions.Add(new BankTransaction(
                booked.Value,
                credit ? amount : -amount,
                label,
                structured ?? (string.Equals(endToEnd, "NOTPROVIDED", StringComparison.OrdinalIgnoreCase) ? null : endToEnd),
                name,
                iban,
                bankReference));
        }

        return transactions;
    }

    private static decimal? AmountOf(XElement detail) =>
        TryAmount(Child(detail, "Amt") ?? Child(Child(Child(detail, "AmtDtls"), "TxAmt"), "Amt"), out decimal amount) ? amount : null;

    private static bool TryAmount(XElement? element, out decimal amount)
    {
        amount = 0m;
        return element is not null
            && decimal.TryParse(element.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && amount > 0m
            && decimal.Round(amount, 2) == amount;
    }

    private static DateOnly? Day(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string? Value(XElement? element) => string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static int LineOf(XElement element) => element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
}
