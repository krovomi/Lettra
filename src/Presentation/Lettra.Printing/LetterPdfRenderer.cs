using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Pdf;

namespace Lettra.Printing;

/// <summary>Une partie d'une lettre : un titre et ses paragraphes (une ligne vide n'est pas un paragraphe).</summary>
public sealed record LetterSection(string Heading, IReadOnlyList<string> Paragraphs);

/// <summary>Un signataire : sa qualité (« Pour le cabinet ») et son nom.</summary>
public sealed record LetterSignature(string Role, string Name);

/// <summary>
/// Une lettre déjà rédigée, dans la langue de <paramref name="Culture"/> : expéditeur, destinataire, lieu et date,
/// titre, parties et signatures. Le rendu ne compose aucun texte ; il met la lettre en page.
/// </summary>
public sealed record PrintableLetter(
    CultureInfo Culture,
    IReadOnlyList<string> Sender,
    IReadOnlyList<string> Recipient,
    string Dateline,
    string Title,
    IReadOnlyList<LetterSection> Sections,
    IReadOnlyList<LetterSignature> Signatures);

/// <summary>Rend une lettre en PDF/A-3b A4. Sans état ; utilisable depuis plusieurs fils.</summary>
public interface ILetterPdfRenderer
{
    byte[] Render(PrintableLetter letter);
}

/// <summary>Mise en page des lettres (lettre de mission) avec PDFsharp/MigraDoc, dans le style des pièces commerciales.</summary>
public sealed class LetterPdfRenderer : ILetterPdfRenderer
{
    private static readonly Color Ink = new(0x1F, 0x29, 0x37);
    private static readonly Color Muted = new(0x6B, 0x72, 0x80);

    public byte[] Render(PrintableLetter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);
        EmbeddedFontResolver.EnsureInstalled();

        MigraDoc.Rendering.PdfDocumentRenderer renderer = new() { Document = Build(letter) };
        renderer.RenderDocument();
        using PdfDocument pdf = renderer.PdfDocument;
        pdf.Info.Title = letter.Title;
        pdf.Info.Author = letter.Sender.Count > 0 ? letter.Sender[0] : string.Empty;
        pdf.Info.Creator = "Lettra";
        return PdfA3.Save(pdf, letter.Culture, []);
    }

    /// <summary>Modèle MigraDoc de la lettre, avant rendu (les tests y lisent les textes).</summary>
    internal static Document Build(PrintableLetter letter)
    {
        Document doc = new();
        doc.Info.Title = letter.Title;
        Style normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        Section section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.2);
        Paragraph pages = section.Footers.Primary.AddParagraph();
        pages.Format.Alignment = ParagraphAlignment.Right;
        pages.Format.Font.Size = 8;
        pages.Format.Font.Color = Muted;
        pages.AddPageField();
        pages.AddText(" / ");
        pages.AddNumPagesField();

        AddParties(section, letter);

        Paragraph dateline = section.AddParagraph(letter.Dateline);
        dateline.Format.Alignment = ParagraphAlignment.Right;
        dateline.Format.SpaceBefore = Unit.FromCentimeter(0.6);

        Paragraph title = section.AddParagraph(letter.Title);
        title.Format.Font.Size = 15;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromCentimeter(0.6);
        title.Format.SpaceAfter = Unit.FromCentimeter(0.4);

        foreach (LetterSection part in letter.Sections)
        {
            Paragraph heading = section.AddParagraph(part.Heading);
            heading.Format.Font.Bold = true;
            heading.Format.Font.Size = 10.5;
            heading.Format.SpaceBefore = Unit.FromPoint(8);
            heading.Format.KeepWithNext = true;
            foreach (string text in part.Paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                section.AddParagraph(text).Format.Alignment = ParagraphAlignment.Justify;
            }
        }

        AddSignatures(section, letter.Signatures);
        return doc;
    }

    private static void AddParties(Section section, PrintableLetter letter)
    {
        Table parties = section.AddTable();
        parties.Borders.Visible = false;
        parties.AddColumn(Unit.FromCentimeter(8.3));
        parties.AddColumn(Unit.FromCentimeter(8.3));
        Row row = parties.AddRow();
        AddLines(row.Cells[0], letter.Sender);
        AddLines(row.Cells[1], letter.Recipient);
    }

    private static void AddLines(Cell cell, IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            Paragraph line = cell.AddParagraph(lines[i]);
            line.Format.SpaceAfter = 0;
            if (i == 0)
            {
                line.Format.Font.Bold = true;
                line.Format.Font.Size = 11;
            }
        }
    }

    private static void AddSignatures(Section section, IReadOnlyList<LetterSignature> signatures)
    {
        if (signatures.Count == 0)
        {
            return;
        }

        Table table = section.AddTable();
        table.Borders.Visible = false;
        table.TopPadding = Unit.FromCentimeter(0.8);
        double width = 16.6 / signatures.Count;
        foreach (LetterSignature _ in signatures)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        Row row = table.AddRow();
        row.KeepWith = 0;
        for (int i = 0; i < signatures.Count; i++)
        {
            row.Cells[i].AddParagraph(signatures[i].Role).Format.Font.Bold = true;
            row.Cells[i].AddParagraph(signatures[i].Name);
            Paragraph space = row.Cells[i].AddParagraph();
            space.Format.SpaceBefore = Unit.FromCentimeter(1.6);
        }
    }
}
