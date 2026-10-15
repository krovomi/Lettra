using System.Globalization;
using System.Text;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Printing;
using Lettra.UI.Shared.Billing;
using Lettra.UI.Tests.Billing;

namespace Lettra.UI.Tests.Printing;

/// <summary>
/// Factur-X : le PDF/A-3b de la pièce, son XML CII (profil EN 16931) joint sous le nom <c>factur-x.xml</c> et la
/// déclaration Factur-X dans les métadonnées XMP. veraPDF (build/verapdf/validate.sh) contrôle en CI les échantillons
/// écrits dans <c>LETTRA_PDF_SAMPLES</c>.
/// </summary>
public sealed class FacturXTests
{
    private static readonly CompanyProfileResponse Company = BillingFixtures.Company();

    private static InvoiceResponse Invoice(InvoiceKindDto kind = InvoiceKindDto.Invoice) => FakeInvoicesGateway.Build(
        kind, InvoiceStatusDto.Validated, kind == InvoiceKindDto.CreditNote ? "FAC-000043" : "FAC-000042",
        new DocumentLineRequest { Designation = "Conseil", Quantity = 3, UnitPriceExclTax = 100m, VatRateCode = "FR-20", UnitCode = "HUR" },
        new DocumentLineRequest { Designation = "Guide « TVA »", Quantity = 2, UnitPriceExclTax = 19.99m, VatRateCode = "FR-5.5", Kind = CatalogItemKindDto.Good });

    private static byte[] FacturX(InvoiceResponse invoice) => new PdfDocumentRenderer().Render(
        PrintableMapper.FromInvoice(invoice, Company, CultureInfo.GetCultureInfo("fr-FR")),
        [PdfAttachment.FacturX(EInvoiceSerializer.Write(invoice, Company, EInvoiceFormatDto.Cii))]);

    [Fact]
    public void Le_PDF_declare_le_Factur_X_et_joint_le_XML_CII()
    {
        string pdf = Encoding.Latin1.GetString(FacturX(Invoice()));

        pdf.Should().Contain("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>")
            .And.Contain("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>")
            .And.Contain("<pdfaSchema:prefix>fx</pdfaSchema:prefix>")
            .And.Contain("<pdfaid:part>3</pdfaid:part>")
            .And.Contain("(factur-x.xml)").And.Contain("/AFRelationship/Data");
    }

    [Fact]
    public void La_table_des_references_croisees_suit_l_insertion()
    {
        byte[] bytes = FacturX(Invoice());
        string pdf = Encoding.Latin1.GetString(bytes);

        // Chaque entrée « n » de la table pointe sur le début de son objet « k 0 obj ».
        int startXref = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
        int xref = int.Parse(pdf[(startXref + 9)..].Trim().Split('\n')[0], CultureInfo.InvariantCulture);
        pdf[xref..].Should().StartWith("xref");
        string[] entries = pdf[xref..startXref].Split('\n').Where(l => l.EndsWith(" n ", StringComparison.Ordinal) || l.EndsWith(" n", StringComparison.Ordinal)).ToArray();
        entries.Should().NotBeEmpty();
        for (int number = 1; number <= entries.Length; number++)
        {
            int offset = int.Parse(entries[number - 1][..10], CultureInfo.InvariantCulture);
            pdf[offset..].Should().StartWith($"{number} 0 obj");
        }
    }

    [Fact]
    public void Sans_XML_Factur_X_le_PDF_ne_se_declare_pas_Factur_X()
    {
        string pdf = Encoding.Latin1.GetString(new PdfDocumentRenderer().Render(PrintableMapper.FromInvoice(Invoice(), Company, CultureInfo.GetCultureInfo("fr-FR"))));

        pdf.Should().NotContain("factur-x");
    }

    [Fact]
    public void Les_echantillons_Factur_X_sont_ecrits_pour_veraPDF()
    {
        string? directory = Environment.GetEnvironmentVariable("LETTRA_PDF_SAMPLES");
        (string Name, byte[] Pdf)[] samples =
        [
            ("Factur-X-facture.pdf", FacturX(Invoice())),
            ("Factur-X-acompte.pdf", FacturX(Invoice(InvoiceKindDto.DownPayment))),
            ("Factur-X-avoir.pdf", FacturX(Invoice(InvoiceKindDto.CreditNote) with { CreditedInvoiceNumber = "FAC-000042", CreditReason = "Erreur de quantité" })),
        ];

        samples.Should().OnlyContain(s => s.Pdf.Length > 1000);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            foreach ((string name, byte[] pdf) in samples)
            {
                File.WriteAllBytes(Path.Combine(directory, name), pdf);
            }
        }
    }
}
