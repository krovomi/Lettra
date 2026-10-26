using System.Globalization;
using Lettra.Printing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;

namespace Lettra.UI.Tests.Printing;

/// <summary>La preuve d'authenticité imprimée : un QR code signé de plus, avec son mode d'emploi, dans la langue du document.</summary>
public sealed class ProofQrTests
{
    private const string Proof = "LETTRA-PREUVE-1;552100554;F-2026-0042;20261016;426.94;p256-0123456789abcdef;MEUCIQ";

    private static int ImagesOf(Document document) =>
        document.Sections.Cast<Section>().SelectMany(s => s.Elements.OfType<Table>())
            .SelectMany(t => t.Rows.Cast<Row>()).SelectMany(r => r.Cells.Cast<Cell>()).Sum(c => c.Elements.OfType<Image>().Count());

    [Fact]
    public void Une_piece_signee_porte_le_QR_code_de_preuve_et_la_cle_qui_l_a_signee()
    {
        PrintableDocument facture = PdfDocumentRendererTests.Facture() with { ProofCode = Proof, ProofKeyId = "p256-0123456789abcdef" };

        Document document = PdfDocumentRenderer.Build(facture);

        ImagesOf(document).Should().Be(ImagesOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture())) + 1, "le QR code de paiement reste, la preuve s'ajoute");
        PdfDocumentRendererTests.TextOf(document).Should().Contain("Preuve d'authenticité").And.Contain("Clé de signature p256-0123456789abcdef");
        new PdfDocumentRenderer().Render(facture).Should().NotBeEmpty();
    }

    [Fact]
    public void La_preuve_est_dite_dans_la_langue_du_document_et_absente_sans_signature()
    {
        PdfDocumentRendererTests.TextOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture(CultureInfo.GetCultureInfo("en-US")) with { ProofCode = Proof }))
            .Should().Contain("Proof of authenticity").And.NotContain("Signing key", "sans version de clé connue, rien n'est imprimé");
        PdfDocumentRendererTests.TextOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture())).Should().NotContain("Preuve d'authenticité");
    }
}
