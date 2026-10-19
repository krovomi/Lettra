using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.Banking.Statements;

namespace Lettra.Exports.Tests.Banking;

/// <summary>Relevés bancaires lus depuis des fichiers : CAMT.053, OFX, CSV des banques françaises.</summary>
public sealed class BankStatementParserTests
{
    private static Result<ParsedBankStatement> Parse(string text, CsvImportOptions? csv = null, Encoding? encoding = null) =>
        BankStatementParser.Parse((encoding ?? Encoding.UTF8).GetBytes(text), csv);

    [Fact]
    public void Un_csv_francais_au_point_virgule_avec_virgule_decimale_et_latin1()
    {
        const string csv = "Date;Libellé;Débit;Crédit\r\n12/10/2026;VIR SEPA DUPONT F2026-0007;;1 200,00\r\n13/10/2026;FRAIS TENUE DE COMPTE;12,50;\r\n";

        ParsedBankStatement statement = Parse(csv, encoding: Encoding.Latin1).Value;

        statement.Format.Should().Be(BankStatementFormat.Csv);
        statement.Transactions.Should().HaveCount(2);
        statement.Transactions[0].Should().Match<BankTransaction>(t => t.BookingDate == new DateOnly(2026, 10, 12) && t.Amount == 1200m && t.Label == "VIR SEPA DUPONT F2026-0007");
        statement.Transactions[1].Amount.Should().Be(-12.50m);
    }

    [Fact]
    public void Un_csv_aux_colonnes_inconnues_se_lit_avec_les_reglages()
    {
        const string csv = "a,b,c\n2026-10-12,VIR MARTIN,80.00\n";

        Parse(csv).Errors.Single().Code.Should().Be(BankStatementErrors.CsvColumnsInvalidCode);
        Parse(csv, new CsvImportOptions { DateColumn = 1, LabelColumn = 2, AmountColumn = 3 }).Value.Transactions.Single().Amount.Should().Be(80m);
    }

    [Fact]
    public void Un_ofx_sgml_est_lu()
    {
        const string ofx = """
            OFXHEADER:100
            DATA:OFXSGML

            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKACCTFROM><ACCTID>12345678901</BANKACCTFROM>
            <BANKTRANLIST><DTSTART>20261001<DTEND>20261031
            <STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261012<TRNAMT>250.00<FITID>A1<NAME>CHQ 1234567<MEMO>REMISE CHEQUE</STMTTRN>
            <STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261013120000<TRNAMT>-3.20<FITID>A2<NAME>COMMISSION</STMTTRN>
            </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """;

        ParsedBankStatement statement = Parse(ofx).Value;

        statement.Format.Should().Be(BankStatementFormat.Ofx);
        statement.Transactions.Select(t => t.Amount).Should().Equal(250m, -3.20m);
        statement.Transactions[0].BookingDate.Should().Be(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void Un_fichier_vide_ou_un_xml_inconnu_est_refuse()
    {
        BankStatementParser.Parse([], null).Errors.Single().Code.Should().Be(BankStatementErrors.FileRequiredCode);
        Parse("<html><body>relevé</body></html>").Errors.Single().Code.Should().Be(BankStatementErrors.FormatUnknownCode);
    }

    [Fact]
    public void Le_releve_de_demonstration_est_un_camt053_lisible_dont_les_operations_trouvent_leurs_factures()
    {
        DateOnly today = new(2026, 10, 16);
        CustomerId dupont = new(Guid.CreateVersion7());
        CustomerId martin = new(Guid.CreateVersion7());
        ReceivableDocument Doc(string number, CustomerId customer, string name, decimal total, int day) =>
            new(new InvoiceId(Guid.CreateVersion7()), InvoiceKind.Invoice, number, customer, name, new DateOnly(2026, 9, day), new DateOnly(2026, 9, day).AddDays(30), total, 0m, 0m, null);
        List<ReceivableDocument> documents =
        [
            Doc("F2026-0001", dupont, "Dupont SARL", 1200m, 1),
            Doc("F2026-0002", martin, "Boulangerie Martin", 480m, 2),
            Doc("F2026-0003", dupont, "Dupont SARL", 900m, 3),
            Doc("F2026-0004", martin, "Boulangerie Martin", 150m, 4),
            Doc("F2026-0005", dupont, "Dupont SARL", 60m, 5),
            Doc("F2026-0006", dupont, "Dupont SARL", 40m, 6),
        ];

        byte[] content = DemoBankStatement.Build(documents, today);
        ParsedBankStatement statement = BankStatementParser.Parse(content, null).Value;

        statement.Format.Should().Be(BankStatementFormat.Camt053);
        statement.Transactions.Should().Contain(t => t.Amount < 0m, "des frais bancaires restent à écarter");
        statement.Transactions.Should().OnlyContain(t => t.BookingDate <= today);

        List<BankLineCandidate> lines = [.. statement.Transactions.Select(t => new BankLineCandidate(Guid.CreateVersion7(), t.BookingDate, t.Amount, t.Label, t.Reference, t.CounterpartyName, null))];
        List<OpenInvoice> open = [.. documents.Select(d => new OpenInvoice(d.InvoiceId.Value, d.Number, d.CustomerId, d.CustomerName, d.IssueDate, d.DueDate, d.Outstanding))];
        IReadOnlyList<MatchProposal> proposals = ReconciliationMatcher.ProposeAll(lines, open, [], 1m);

        proposals.Select(p => p.Rule).Should().Contain([MatchRule.InvoiceNumber, MatchRule.AmountAndName]);
        proposals.Count.Should().BeGreaterThanOrEqualTo(3);
        DemoBankStatement.FileName(today).Should().Be("releve-demo-2026-10-16.xml");
    }
}
