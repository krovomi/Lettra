using System.Xml.Linq;
using FluentAssertions;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests;

/// <summary>
/// Les mentions de la norme française XP Z12-012 (BR-FR) : les trois notes codées de paiement (BR-FR-05) et la date
/// de la facture d'origine d'un avoir (BR-FR-CO-05), en CII comme en UBL. Le Schematron BR-FR les contrôle en CI.
/// </summary>
public sealed class EInvoiceNotesTests
{
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    [Fact]
    public void Les_mentions_de_paiement_figees_portent_leur_code_sujet()
    {
        InvoiceResponse invoice = PlatformSamples.Invoice("FAC-000010") with
        {
            LegalMentions =
            [
                "Option pour le paiement de la TVA d'après les débits",
                "Pénalités de retard : 12 % par an",
                "Indemnité forfaitaire pour frais de recouvrement en cas de retard de paiement : 40 €",
                "Escompte de 2 % pour paiement sous 8 jours",
            ],
        };

        Dictionary<string, string> cii = CiiNotes(invoice);

        cii.Should().Contain("PMD", "Pénalités de retard : 12 % par an")
            .And.Contain("PMT", "Indemnité forfaitaire pour frais de recouvrement en cas de retard de paiement : 40 €")
            .And.Contain("AAB", "Escompte de 2 % pour paiement sous 8 jours")
            .And.Contain(string.Empty, "Option pour le paiement de la TVA d'après les débits");
        UblNotes(invoice).Should().Contain(
            "#PMD#Pénalités de retard : 12 % par an",
            "#AAB#Escompte de 2 % pour paiement sous 8 jours",
            "Option pour le paiement de la TVA d'après les débits");
    }

    [Fact]
    public void Une_mention_absente_de_la_piece_est_ecrite_telle_que_la_loi_s_applique()
    {
        Dictionary<string, string> cii = CiiNotes(PlatformSamples.Invoice("FAC-000011"));

        cii["AAB"].Should().Be("Pas d'escompte pour paiement anticipé");
        cii["PMD"].Should().Contain("BCE majoré de 10 points");
        cii["PMT"].Should().Contain("non applicable");
    }

    [Fact]
    public void Un_avoir_reference_la_facture_d_origine_avec_sa_date()
    {
        InvoiceResponse credit = PlatformSamples.Invoice("AV-000001", InvoiceKindDto.CreditNote);
        DateOnly original = new(2026, 9, 30);

        XElement cii = XDocument.Parse(Text(EInvoiceSerializer.Write(credit, null, EInvoiceFormatDto.Cii, original))).Root!;
        XElement reference = cii.Descendants(Ram + "InvoiceReferencedDocument").Single();
        reference.Element(Ram + "IssuerAssignedID")!.Value.Should().Be("FAC-000001");
        reference.Element(Ram + "FormattedIssueDateTime")!.Element(Qdt + "DateTimeString")!.Value.Should().Be("20260930");
        CiiNotes(credit)["PMD"].Should().Contain("sans objet");

        XElement ubl = XDocument.Parse(Text(EInvoiceSerializer.Write(credit, null, EInvoiceFormatDto.Ubl, original))).Root!;
        XElement billing = ubl.Element(Cac + "BillingReference")!.Element(Cac + "InvoiceDocumentReference")!;
        billing.Element(Cbc + "ID")!.Value.Should().Be("FAC-000001");
        billing.Element(Cbc + "IssueDate")!.Value.Should().Be("2026-09-30");
    }

    private static Dictionary<string, string> CiiNotes(InvoiceResponse invoice) =>
        XDocument.Parse(Text(EInvoiceSerializer.Write(invoice, null, EInvoiceFormatDto.Cii)))
            .Descendants(Ram + "IncludedNote")
            .GroupBy(n => n.Element(Ram + "SubjectCode")?.Value ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count() == 1 || g.Key.Length == 0 ? g.Last().Element(Ram + "Content")!.Value : throw new InvalidOperationException($"Code {g.Key} en double."));

    private static List<string> UblNotes(InvoiceResponse invoice) =>
        [.. XDocument.Parse(Text(EInvoiceSerializer.Write(invoice, null, EInvoiceFormatDto.Ubl))).Root!.Elements(Cbc + "Note").Select(n => n.Value)];

    private static string Text(byte[] content) => System.Text.Encoding.UTF8.GetString(content);
}
