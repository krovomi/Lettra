using System.Globalization;
using System.Text;
using Lettra.Printing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;

namespace Lettra.UI.Tests.Printing;

public sealed class PdfDocumentRendererTests
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    internal static PrintableDocument Facture(CultureInfo? culture = null) => new()
    {
        Kind = PrintableDocumentKind.Invoice,
        Number = "F-2026-0042",
        IssueDate = new DateOnly(2026, 10, 14),
        DeliveryDate = new DateOnly(2026, 10, 10),
        DueDate = new DateOnly(2026, 11, 13),
        Seller = new PrintableParty
        {
            Name = "Atelier Dupont",
            AddressLines = ["12 rue de la Paix", "75002 Paris"],
            Siren = "123456789",
            VatNumber = "FR12123456789",
            LegalForm = "SAS",
            ShareCapital = "10 000 €",
            Registration = "RCS Paris",
        },
        Buyer = new PrintableParty
        {
            Name = "Boulangerie Martin",
            AddressLines = ["3 place du Marché", "69001 Lyon"],
            Siren = "987654321",
            VatNumber = "FR45987654321",
        },
        Lines =
        [
            new("DEV-01", "Développement", 3m, "heure", 120m, 20m),
            new("FORM", "Formation", 1m, "jour", 99.99m, 5.5m, 10m),
        ],
        PaymentTerms = "Paiement à 30 jours par virement.",
        LegalMentions = ["Pénalités de retard : trois fois le taux d'intérêt légal.", "Indemnité forfaitaire pour frais de recouvrement : 40 €."],
        Iban = "FR76 3000 6000 0112 3456 7890 189",
        Culture = culture ?? Fr,
    };

    [Fact]
    public void Le_rendu_produit_un_PDF()
    {
        byte[] pdf = new PdfDocumentRenderer().Render(Facture());

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void La_facture_porte_les_mentions_attendues_en_francais()
    {
        string text = TextOf(PdfDocumentRenderer.Build(Facture()));

        text.Should().Contain("FACTURE").And.Contain("N° F-2026-0042")
            .And.Contain("Date d'émission").And.Contain("14/10/2026")
            .And.Contain("Date de la vente ou de la prestation").And.Contain("10/10/2026")
            .And.Contain("Date d'échéance").And.Contain("13/11/2026")
            .And.Contain("SIREN 987 654 321").And.Contain("N° TVA intracommunautaire FR45987654321")
            .And.Contain("SAS au capital de 10 000 €").And.Contain("RCS Paris")
            .And.Contain("Paiement à 30 jours par virement.")
            .And.Contain("Indemnité forfaitaire pour frais de recouvrement : 40 €.");
    }

    [Fact]
    public void Les_totaux_sont_ceux_du_calcul_partage_ventiles_par_taux()
    {
        // 3 × 120 = 360,00 à 20 % ; 99,99 − 10 % = 89,991 → 89,99 à 5,5 % (TVA 4,95).
        string text = TextOf(PdfDocumentRenderer.Build(Facture()));

        text.Should().Contain("360,00 €").And.Contain("89,99 €")
            .And.Contain("72,00 €").And.Contain("4,95 €")
            .And.Contain("449,99 €")    // total HT
            .And.Contain("76,95 €")     // total TVA
            .And.Contain("526,94 €")    // total TTC
            .And.Contain("5,5 %").And.Contain("Remise");
    }

    [Fact]
    public void Le_document_est_imprime_dans_la_langue_du_client()
    {
        string text = TextOf(PdfDocumentRenderer.Build(Facture(En)));

        text.Should().Contain("INVOICE").And.Contain("No. F-2026-0042").And.Contain("Issue date").And.Contain("10/14/2026")
            .And.Contain("€526.94").And.NotContain("Date d'émission");
    }

    [Fact]
    public void Un_brouillon_porte_un_filigrane_et_aucun_numero()
    {
        string text = TextOf(PdfDocumentRenderer.Build(Facture() with { Number = null }));

        text.Should().Contain("BROUILLON").And.Contain("sans numéro ni valeur comptable").And.NotContain("N° F-");
    }

    [Fact]
    public void Un_devis_affiche_sa_validite_et_le_bon_pour_accord()
    {
        PrintableDocument devis = Facture() with { Kind = PrintableDocumentKind.Quote, Number = "D-2026-0007" };

        string text = TextOf(PdfDocumentRenderer.Build(devis));

        text.Should().Contain("DEVIS").And.Contain("Valable jusqu'au").And.Contain("Bon pour accord").And.NotContain("Date d'échéance");
    }

    [Fact]
    public void Un_avoir_rappelle_la_facture_d_origine()
    {
        PrintableDocument avoir = Facture() with
        {
            Kind = PrintableDocumentKind.CreditNote,
            Number = "AV-2026-0003",
            RelatedDocumentNumber = "F-2026-0042",
            Lines = [new(null, "Retour formation", -1m, "jour", 99.99m, 5.5m)],
        };

        string text = TextOf(PdfDocumentRenderer.Build(avoir));

        text.Should().Contain("AVOIR").And.Contain("Avoir sur la facture n° F-2026-0042").And.Contain("-105,49 €");
    }

    [Fact]
    public void Les_acomptes_verses_sont_deduits_du_net_a_payer()
    {
        PrintableDocument solde = Facture() with { Deductions = [new("FA-2026-0001", new DateOnly(2026, 9, 1), 100m)] };

        string text = TextOf(PdfDocumentRenderer.Build(solde));

        text.Should().Contain("Acompte n° FA-2026-0001 du 01/09/2026").And.Contain("-100,00 €")
            .And.Contain("Net à payer").And.Contain("426,94 €");
    }

    [Fact]
    public void Un_document_de_mille_lignes_se_rend_sur_plusieurs_pages()
    {
        PrintableLine[] lignes = Enumerable.Range(1, 1000).Select(i => new PrintableLine($"R{i}", $"Ligne {i}", 1m, "unité", 1m, 20m)).ToArray();

        byte[] pdf = new PdfDocumentRenderer().Render(Facture() with { Lines = lignes });

        Encoding.ASCII.GetString(pdf).Should().Contain("/Count ");
        pdf.Length.Should().BeGreaterThan(20_000);
    }

    [Fact]
    public void Les_textes_d_impression_existent_en_francais_et_en_anglais()
    {
        foreach (PrintableDocumentKind kind in Enum.GetValues<PrintableDocumentKind>())
        {
            PrintableDocument document = Facture() with { Kind = kind, RelatedDocumentNumber = "X-1" };
            Action fr = () => PdfDocumentRenderer.Build(document);
            Action en = () => PdfDocumentRenderer.Build(document with { Culture = En });
            fr.Should().NotThrow();
            en.Should().NotThrow();
        }
    }

    /// <summary>Tous les textes du modèle MigraDoc, en-têtes et pieds de page compris.</summary>
    internal static string TextOf(Document document)
    {
        StringBuilder text = new();
        foreach (Section section in document.Sections.Cast<Section>())
        {
            Walk(section.Headers.Primary.Elements, text);
            Walk(section.Elements, text);
            Walk(section.Footers.Primary.Elements, text);
        }

        return text.ToString();
    }

    private static void Walk(DocumentElements elements, StringBuilder text)
    {
        foreach (DocumentObject element in elements.Cast<DocumentObject>())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    WalkParagraph(paragraph.Elements, text);
                    text.Append('\n');
                    break;
                case Table table:
                    foreach (Row row in table.Rows.Cast<Row>())
                    {
                        foreach (Cell cell in row.Cells.Cast<Cell>())
                        {
                            Walk(cell.Elements, text);
                        }
                    }

                    break;
                case TextFrame frame:
                    Walk(frame.Elements, text);
                    break;
            }
        }
    }

    private static void WalkParagraph(ParagraphElements elements, StringBuilder text)
    {
        foreach (DocumentObject element in elements.Cast<DocumentObject>())
        {
            switch (element)
            {
                case Text t:
                    text.Append(t.Content);
                    break;
                case Character { SymbolName: SymbolName.Blank }:
                    text.Append(' ');
                    break;
                case FormattedText formatted:
                    WalkParagraph(formatted.Elements, text);
                    break;
            }
        }
    }
}
