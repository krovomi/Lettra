using System.Globalization;
using System.Text;
using Lettra.Printing;

namespace Lettra.UI.Tests.Printing;

/// <summary>
/// PDF/A-3b : identification XMP et fichiers joints. La validation complète est faite par veraPDF en CI
/// (build/verapdf/validate.sh) sur les échantillons que ce test écrit quand <c>LETTRA_PDF_SAMPLES</c> est défini.
/// </summary>
public sealed class PdfAConformanceTests
{
    private static readonly PdfAttachment FacturX = new("factur-x.xml", "<rsm:CrossIndustryInvoice/>"u8.ToArray(), "text/xml",
        PdfAttachmentRelationship.Data, "Factur-X");

    [Fact]
    public void Le_PDF_se_declare_PDF_A_3b_dans_ses_metadonnees()
    {
        string pdf = Encoding.Latin1.GetString(new PdfDocumentRenderer().Render(PdfDocumentRendererTests.Facture()));

        pdf.Should().Contain("<pdfaid:part>3</pdfaid:part>").And.Contain("<pdfaid:conformance>B</pdfaid:conformance>")
            .And.Contain("/OutputIntents").And.Contain("/Lang(fr-FR)");
    }

    [Fact]
    public void Un_fichier_joint_est_declare_comme_fichier_associe()
    {
        string pdf = Encoding.Latin1.GetString(new PdfDocumentRenderer().Render(PdfDocumentRendererTests.Facture(), [FacturX]));

        pdf.Should().Contain("(factur-x.xml)").And.Contain("/AFRelationship/Data").And.Contain("/AF[")
            .And.Contain("/Subtype/text#2Fxml").And.Contain("/UF(factur-x.xml)");
    }

    [Fact]
    public void Les_echantillons_sont_ecrits_pour_veraPDF()
    {
        string? directory = Environment.GetEnvironmentVariable("LETTRA_PDF_SAMPLES");
        PdfDocumentRenderer renderer = new();
        List<(string Name, byte[] Pdf)> samples = [];
        foreach (PrintableDocumentKind kind in Enum.GetValues<PrintableDocumentKind>())
        {
            PrintableDocument document = PdfDocumentRendererTests.Facture() with { Kind = kind, RelatedDocumentNumber = "X-1" };
            samples.Add(($"{kind}-fr.pdf", renderer.Render(document)));
            samples.Add(($"{kind}-en.pdf", renderer.Render(document with { Culture = CultureInfo.GetCultureInfo("en-US") })));
        }

        samples.Add(("Invoice-brouillon.pdf", renderer.Render(PdfDocumentRendererTests.Facture() with { Number = null })));
        samples.Add(("Invoice-factur-x.pdf", renderer.Render(PdfDocumentRendererTests.Facture(), [FacturX])));

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
