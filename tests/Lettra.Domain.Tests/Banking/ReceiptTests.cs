using Lettra.Domain.Banking;

namespace Lettra.Domain.Tests.Banking;

/// <summary>Pièces justificatives : quelles opérations en appellent une, leur cycle, et les fichiers acceptés.</summary>
public sealed class ReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
    private static readonly byte[] Heic = [0, 0, 0, 0x18, .. "ftypheic"u8, 0, 0];

    private static BankStatementLine Line(decimal amount) =>
        BankStatementLine.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new BankTransaction(new DateOnly(2026, 10, 1), amount, "CB FOURNITURES", null, "Fournitures Plus", null, null), Guid.NewGuid().ToString("N"), null);

    [Fact]
    public void Une_depense_reclamee_puis_relancee_garde_la_derniere_adresse_et_compte_les_demandes()
    {
        BankStatementLine line = Line(-46.80m);
        long version = line.Version;

        line.ReceiptStatus.Should().Be(ReceiptStatus.Missing);
        line.RequestReceipt("a@b.example", User, Now).IsSuccess.Should().BeTrue();
        line.RequestReceipt("c@d.example", User, Now.AddDays(7)).IsSuccess.Should().BeTrue();

        line.ReceiptStatus.Should().Be(ReceiptStatus.Requested);
        line.ReceiptRequests.Should().Be(2);
        line.ReceiptRequestedTo.Should().Be("c@d.example");
        line.ReceiptRequestedAt.Should().Be(Now.AddDays(7));
        line.Version.Should().Be(version + 2);
    }

    [Fact]
    public void Un_credit_n_appelle_aucune_piece()
    {
        BankStatementLine line = Line(120m);

        line.RequestReceipt("a@b.example", User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(ReceiptErrors.LineNotDebitCode);
        line.SetReceiptNotNeeded(true, User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(ReceiptErrors.LineNotDebitCode);
        line.ReceiptReceived(User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(ReceiptErrors.LineNotDebitCode);
    }

    [Fact]
    public void Une_piece_deposee_ne_se_reclame_plus_mais_se_remplace()
    {
        BankStatementLine line = Line(-950m);
        line.RequestReceipt("a@b.example", User, Now);

        line.ReceiptReceived(User, Now).IsSuccess.Should().BeTrue();
        line.ReceiptReceived(User, Now).IsSuccess.Should().BeTrue("un nouveau dépôt remplace le premier");

        line.ReceiptStatus.Should().Be(ReceiptStatus.Received);
        line.RequestReceipt("a@b.example", User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(ReceiptErrors.AlreadyReceivedCode);
        line.SetReceiptNotNeeded(true, User, Now).Errors.Should().ContainSingle().Which.Code.Should().Be(ReceiptErrors.AlreadyReceivedCode);
    }

    [Fact]
    public void Non_attendue_puis_de_nouveau_attendue_revient_a_son_etat_d_avant()
    {
        BankStatementLine missing = Line(-12.50m);
        BankStatementLine requested = Line(-84.60m);
        requested.RequestReceipt("a@b.example", User, Now);

        missing.SetReceiptNotNeeded(true, User, Now);
        requested.SetReceiptNotNeeded(true, User, Now);
        missing.ReceiptStatus.Should().Be(ReceiptStatus.NotNeeded);
        missing.SetReceiptNotNeeded(false, User, Now);
        requested.SetReceiptNotNeeded(false, User, Now);

        missing.ReceiptStatus.Should().Be(ReceiptStatus.Missing);
        requested.ReceiptStatus.Should().Be(ReceiptStatus.Requested);
    }

    [Fact]
    public void Le_type_se_lit_dans_le_contenu_et_jamais_dans_le_nom()
    {
        ReceiptFiles.Sniff(Jpeg)!.ContentType.Should().Be("image/jpeg");
        ReceiptFiles.Sniff(Png)!.ContentType.Should().Be("image/png");
        ReceiptFiles.Sniff(Pdf)!.ContentType.Should().Be("application/pdf");
        ReceiptFiles.Sniff(Webp)!.ContentType.Should().Be("image/webp");
        ReceiptFiles.Sniff(Heic)!.ContentType.Should().Be("image/heic");
        ReceiptFiles.Sniff("<svg onload=alert(1)>"u8).Should().BeNull("un SVG peut porter du script");
        ReceiptFiles.Sniff("MZ\x90\0"u8).Should().BeNull();
        ReceiptFiles.Sniff([]).Should().BeNull();

        BankLineReceipt receipt = BankLineReceipt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "facture.pdf", Jpeg, User, Now).Value;
        receipt.ContentType.Should().Be("image/jpeg", "le nom ne décide pas du type");
        receipt.Size.Should().Be(Jpeg.Length);
    }

    [Fact]
    public void Un_fichier_vide_trop_lourd_ou_inconnu_est_refuse_et_le_nom_est_nettoye()
    {
        Guid line = Guid.CreateVersion7();
        byte[] tooLarge = new byte[BankLineReceipt.MaxContentLength + 1];
        Png.CopyTo(tooLarge, 0);

        BankLineReceipt.Create(Guid.CreateVersion7(), line, "a.png", [], User, Now).Errors.Single().Code.Should().Be(ReceiptErrors.FileRequiredCode);
        BankLineReceipt.Create(Guid.CreateVersion7(), line, "a.png", null, User, Now).Errors.Single().Code.Should().Be(ReceiptErrors.FileRequiredCode);
        BankLineReceipt.Create(Guid.CreateVersion7(), line, "a.png", tooLarge, User, Now).Errors.Single().Code.Should().Be(ReceiptErrors.FileTooLargeCode);
        BankLineReceipt.Create(Guid.CreateVersion7(), line, "a.png", "texte"u8.ToArray(), User, Now).Errors.Single().Code.Should().Be(ReceiptErrors.FileTypeInvalidCode);

        BankLineReceipt.Create(Guid.CreateVersion7(), line, "../../etc/ticket.png", Png, User, Now).Value.FileName.Should().Be("ticket.png");
        BankLineReceipt.Create(Guid.CreateVersion7(), line, "  ", Pdf, User, Now).Value.FileName.Should().Be("justificatif.pdf");
        BankLineReceipt.Create(Guid.CreateVersion7(), line, new string('a', 300) + ".png", Png, User, Now).Value.FileName.Should().HaveLength(BankLineReceipt.MaxFileNameLength);
    }

    [Fact]
    public void Montant_date_et_TVA_confirmes_sont_bornes_et_gardes_avec_la_piece()
    {
        DateOnly today = new(2026, 10, 26);
        ReceiptFigures figures = ReceiptFigures.Create(84.90m, new DateOnly(2026, 10, 15), 14.15m, readOnDevice: true, today).Value;
        BankLineReceipt receipt = BankLineReceipt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "ticket.jpg", Jpeg, User, Now).Value;

        receipt.Figures.Should().BeNull();
        receipt.Confirm(figures);
        receipt.Figures.Should().Be(new ReceiptFigures(84.90m, new DateOnly(2026, 10, 15), 14.15m, true));

        ReceiptFigures.Create(0m, today, 0m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.AmountInvalid);
        ReceiptFigures.Create(10.001m, today, 0m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.AmountInvalid);
        ReceiptFigures.Create(ReceiptFigures.MaxAmount + 1m, today, 0m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.AmountInvalid);
        ReceiptFigures.Create(10m, today, 10.01m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.VatInvalid);
        ReceiptFigures.Create(10m, today, -1m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.VatInvalid);
        ReceiptFigures.Create(10m, today, 1.005m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.VatInvalid);
        ReceiptFigures.Create(10m, today.AddDays(1), 0m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.DateInvalid);
        ReceiptFigures.Create(10m, new DateOnly(1999, 12, 31), 0m, false, today).Errors.Should().ContainSingle().Which.Should().Be(ReceiptErrors.DateInvalid);
        ReceiptFigures.Create(10m, ReceiptFigures.MinDate, 0m, false, today).IsSuccess.Should().BeTrue("une TVA nulle est permise");
        ReceiptErrors.AmountInvalid.Target.Should().Be("Figures.AmountInclTax");
    }
}
