using Lettra.Domain.Banking;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Tests.Banking;

/// <summary>L'e-mail qui réclame les pièces : les dépenses, dans l'ordre du relevé, et l'écran où les déposer.</summary>
public sealed class ReceiptRequestMailerTests
{
    private static BankStatementLine Line(int day, decimal amount, string label, string? name) =>
        BankStatementLine.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new BankTransaction(new DateOnly(2026, 10, day), amount, label, null, name, null, null), Guid.NewGuid().ToString("N"), null);

    [Fact]
    public void Le_message_liste_les_depenses_en_francais_puis_en_anglais_et_mene_a_l_ecran_de_depot()
    {
        BankStatementLine[] lines = [Line(17, -950m, "VIR LOYER SCI DU PORT", "SCI du Port"), Line(9, -84.60m, "PRLV SEPA ENERGIE", null)];

        string body = ReceiptRequestMailer.Body(lines, "  Merci pour la clôture.  ", new Uri("https://lettra.example/"));

        ReceiptRequestMailer.Subject(2).Should().Be("Pièces justificatives à fournir (2) / Receipts to provide (2)");
        body.Should().Contain("Merci pour la clôture.").And.Contain("https://lettra.example/my-receipts");
        body.IndexOf("09/10/2026", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("17/10/2026", StringComparison.Ordinal), "dans l'ordre du relevé");
        body.Should().Contain("PRLV SEPA ENERGIE").And.Contain("SCI du Port").And.Contain("950,00").And.Contain("€950.00");
        body.Should().Contain("Your accounting firm needs the receipts");
    }

    [Fact]
    public void Sans_adresse_publique_le_message_nomme_l_ecran()
    {
        string body = ReceiptRequestMailer.Body([Line(1, -12.50m, "FRAIS", null)], null, null);

        body.Should().Contain("écran « Pièces à fournir »").And.NotContain("http");
    }
}
