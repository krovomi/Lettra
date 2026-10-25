using System.Globalization;
using Lettra.Printing;
using Lettra.Printing.Payments;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;

namespace Lettra.UI.Tests.Printing;

/// <summary>QR code de virement EPC : contenu du standard, encodeur QR (niveau M) et images.</summary>
public sealed class PaymentQrTests
{
    private const string Iban = "FR76 3000 6000 0112 3456 7890 189";

    [Fact]
    public void Le_contenu_suit_le_standard_EPC_version_002()
    {
        string? payload = EpcPaymentCode.Payload("Atelier Dupont", Iban, "agrifrpp", 1234.5m, "F-2026-0042");

        payload.Should().Be("BCD\n002\n1\nSCT\nAGRIFRPP\nAtelier Dupont\nFR7630006000011234567890189\nEUR1234.50\n\n\nF-2026-0042");
    }

    [Fact]
    public void Le_BIC_est_facultatif_et_ignore_s_il_est_mal_forme()
    {
        EpcPaymentCode.Payload("Atelier", Iban, null, 10m, "F-1")!.Split('\n')[4].Should().BeEmpty();
        EpcPaymentCode.Payload("Atelier", Iban, "XX", 10m, "F-1")!.Split('\n')[4].Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FR76 3000 6000 0112 3456 7890 188")]
    [InlineData("FR7630006000")]
    [InlineData("1R7630006000011234567890189")]
    [InlineData("FR7630006000011234567890-89")]
    public void Sans_IBAN_valide_il_n_y_a_pas_de_QR_code(string? iban) =>
        EpcPaymentCode.Payload("Atelier", iban, null, 10m, "F-1").Should().BeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.004)]
    [InlineData(1_000_000_000)]
    public void Un_montant_hors_des_bornes_du_standard_n_a_pas_de_QR_code(decimal amount) =>
        EpcPaymentCode.Payload("Atelier", Iban, null, amount, "F-1").Should().BeNull();

    [Fact]
    public void Un_beneficiaire_sans_nom_n_a_pas_de_QR_code() =>
        EpcPaymentCode.Payload(" \n ", Iban, null, 10m, "F-1").Should().BeNull();

    [Fact]
    public void Le_nom_et_le_libelle_tiennent_sur_une_ligne_et_dans_les_longueurs_du_standard()
    {
        string[] lines = EpcPaymentCode.Payload(new string('A', 90) + "\nB", Iban, null, 10m, "Facture\r\n" + new string('x', 200))!.Split('\n');

        lines.Should().HaveCount(11);
        lines[5].Should().HaveLength(70);
        lines[10].Should().StartWith("Facture x").And.HaveLength(140);
    }

    [Theory]
    [InlineData("FR7630006000011234567890189")]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("be71096123456769")]
    public void Un_IBAN_juste_est_normalise(string iban) =>
        EpcPaymentCode.NormalizedIban(iban).Should().MatchRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]+$");

    [Fact]
    public void Reed_Solomon_donne_les_octets_de_correction_de_la_norme()
    {
        // ISO/IEC 18004, annexe I : « 01234567 » en version 1-M.
        byte[] data = [0x10, 0x20, 0x0C, 0x56, 0x61, 0x80, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11];

        ReedSolomon.Remainder(data, ReedSolomon.Divisor(10)).Should()
            .Equal(0xA5, 0x24, 0xD4, 0xC1, 0xED, 0x36, 0xC7, 0x87, 0x2C, 0x55);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(200, 10)]
    [InlineData(331, 13)]
    [InlineData(332, 14)]
    [InlineData(2331, 40)]
    public void La_version_est_la_plus_petite_qui_contient_le_texte(int length, int version)
    {
        QrCode qr = QrCode.Encode(new string('a', length));

        qr.Version.Should().Be(version);
        qr.Size.Should().Be((version * 4) + 17);
        qr.Mask.Should().BeInRange(0, 7);
    }

    [Fact]
    public void Un_texte_trop_long_est_refuse() =>
        FluentActions.Invoking(() => QrCode.Encode(new string('a', 2332))).Should().Throw<ArgumentException>();

    [Fact]
    public void Les_reperes_de_position_sont_aux_trois_coins()
    {
        QrCode qr = QrCode.Encode("BCD");

        foreach ((int x, int y) in new[] { (0, 0), (qr.Size - 7, 0), (0, qr.Size - 7) })
        {
            qr.IsDark(x, y).Should().BeTrue();
            qr.IsDark(x + 1, y + 1).Should().BeFalse();
            qr.IsDark(x + 3, y + 3).Should().BeTrue();
        }

        qr.IsDark(8, qr.Size - 8).Should().BeTrue("le module noir fixe est toujours là");
        qr.IsDark(-1, 0).Should().BeFalse();
        qr.IsDark(0, qr.Size).Should().BeFalse();
    }

    [Fact]
    public void L_image_PNG_porte_la_marge_blanche()
    {
        QrCode qr = QrCode.Encode("BCD\n002");
        byte[] png = QrImages.Png(qr, scale: 2);

        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        int side = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        side.Should().Be((qr.Size + 8) * 2);
        QrImages.ViewBoxSize(qr).Should().Be(qr.Size + 8);
        QrImages.SvgPath(qr).Should().StartWith("M4,4h1v1h-1z");
    }

    [Fact]
    public void Les_echantillons_sont_ecrits_pour_un_lecteur_de_QR_code()
    {
        string? directory = Environment.GetEnvironmentVariable("LETTRA_QR_SAMPLES");
        Dictionary<string, string> samples = new()
        {
            ["epc"] = EpcPaymentCode.Payload("Atelier Dupont — Société à responsabilité", Iban, "AGRIFRPP", 1234.5m, "F-2026-0042")!,
            ["v1"] = "Lettra",
            ["v7"] = new string('7', 130),
            ["v13"] = new string('é', 165),
            ["v27"] = string.Concat(Enumerable.Range(0, 1000).Select(i => (char)('a' + (i % 26)))),
        };

        foreach ((string name, string text) in samples)
        {
            byte[] png = QrImages.Png(QrCode.Encode(text), scale: 4);
            png.Length.Should().BeGreaterThan(100);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), png);
                File.WriteAllText(Path.Combine(directory, name + ".txt"), text);
            }
        }
    }

    [Fact]
    public void La_facture_porte_le_QR_code_du_net_a_payer_avec_son_numero_en_libelle()
    {
        PrintableDocument facture = PdfDocumentRendererTests.Facture() with
        {
            Bic = "AGRIFRPP",
            Deductions = [new("FA-2026-0001", new DateOnly(2026, 9, 1), 100m)],
        };

        PdfDocumentRenderer.PaymentCode(facture, 426.94m).Should()
            .Be("BCD\n002\n1\nSCT\nAGRIFRPP\nAtelier Dupont\nFR7630006000011234567890189\nEUR426.94\n\n\nF-2026-0042");
        Document document = PdfDocumentRenderer.Build(facture);
        QrImagesOf(document).Should().ContainSingle();
        PdfDocumentRendererTests.TextOf(document).Should().Contain("Scanner pour payer").And.Contain("le virement s'ouvre déjà rempli")
            .And.Contain("IBAN FR76 3000 6000 0112 3456 7890 189").And.Contain("Paiement à 30 jours par virement.");
    }

    [Fact]
    public void Le_QR_code_est_traduit_dans_la_langue_du_document() =>
        PdfDocumentRendererTests.TextOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture(CultureInfo.GetCultureInfo("en-US"))))
            .Should().Contain("Scan to pay").And.Contain("your banking app");

    [Fact]
    public void Un_acompte_porte_aussi_le_QR_code() =>
        QrImagesOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture() with { Kind = PrintableDocumentKind.DownPaymentInvoice }))
            .Should().ContainSingle();

    [Theory]
    [InlineData(PrintableDocumentKind.Quote, "D-1")]
    [InlineData(PrintableDocumentKind.CreditNote, "AV-1")]
    [InlineData(PrintableDocumentKind.Invoice, null)]
    public void Ni_devis_ni_avoir_ni_brouillon_ne_portent_de_QR_code(PrintableDocumentKind kind, string? number)
    {
        PrintableDocument document = PdfDocumentRendererTests.Facture() with { Kind = kind, Number = number };

        PdfDocumentRenderer.PaymentCode(document, 100m).Should().BeNull();
        Document built = PdfDocumentRenderer.Build(document);
        QrImagesOf(built).Should().BeEmpty();
        PdfDocumentRendererTests.TextOf(built).Should().Contain("IBAN FR76");
    }

    [Fact]
    public void Sans_IBAN_valide_ou_hors_euro_la_facture_n_a_pas_de_QR_code()
    {
        QrImagesOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture() with { Iban = "FR00 1234" })).Should().BeEmpty();
        QrImagesOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture() with { Currency = "CHF" })).Should().BeEmpty();
        QrImagesOf(PdfDocumentRenderer.Build(PdfDocumentRendererTests.Facture() with { Iban = null })).Should().BeEmpty();
    }

    private static List<Image> QrImagesOf(Document document) =>
        [.. document.Sections.Cast<Section>().SelectMany(s => s.Elements.OfType<Table>())
            .SelectMany(t => t.Rows.Cast<Row>()).SelectMany(r => r.Cells.Cast<Cell>()).SelectMany(c => c.Elements.OfType<Image>())];
}
