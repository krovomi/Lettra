using System.Globalization;
using System.Resources;
using Lettra.Calculations;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Pdf;

namespace Lettra.Printing;

/// <summary>Rend un document commercial en PDF. Sans état ; utilisable depuis plusieurs fils.</summary>
public interface IDocumentPdfRenderer
{
    byte[] Render(PrintableDocument document);
}

/// <summary>
/// Rendu PDF A4 avec PDFsharp/MigraDoc (MIT). Les totaux et la ventilation de TVA sont recalculés par
/// <see cref="DocumentCalculator"/> : le PDF montre exactement les montants que le serveur retient.
/// Textes dans la langue du document (<see cref="PrintableDocument.Culture"/>), pas dans celle de l'utilisateur.
/// </summary>
public sealed class PdfDocumentRenderer : IDocumentPdfRenderer
{
    private static readonly ResourceManager Texts = new("Lettra.Printing.Resources.PrintResource", typeof(PdfDocumentRenderer).Assembly);

    private static readonly Color Ink = new(0x1F, 0x29, 0x37);
    private static readonly Color Muted = new(0x6B, 0x72, 0x80);
    private static readonly Color Rule = new(0xD1, 0xD5, 0xDB);
    private static readonly Color Band = new(0xF3, 0xF4, 0xF6);

    public byte[] Render(PrintableDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EmbeddedFontResolver.EnsureInstalled();

        MigraDoc.Rendering.PdfDocumentRenderer renderer = new() { Document = Build(document) };
        renderer.RenderDocument();
        using PdfDocument pdf = renderer.PdfDocument;
        pdf.Info.Title = Title(document);
        pdf.Info.Author = document.Seller.Name;
        pdf.Info.Creator = "Lettra";
        using MemoryStream output = new();
        pdf.Save(output, false);
        return output.ToArray();
    }

    /// <summary>Modèle MigraDoc du document, avant rendu (les tests y lisent les textes).</summary>
    internal static Document Build(PrintableDocument document)
    {
        CultureInfo culture = document.Culture;
        DocumentTotals totals = DocumentCalculator.Compute(document.Lines.Select(ToInput));

        Document doc = new();
        doc.Info.Title = Title(document);
        DefineStyles(doc);

        Section section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.4);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

        AddFooter(section, document, culture);
        if (document.Number is null)
        {
            AddDraftWatermark(section, culture);
        }

        AddHeader(section, document, culture);
        AddLines(section, document, culture);
        AddTotals(section, document, totals, culture);
        AddPaymentAndMentions(section, document, culture);
        return doc;
    }

    private static string Title(PrintableDocument document)
    {
        string title = Text(document.Culture, "Title." + document.Kind);
        return document.Number is null ? title : $"{title} {document.Number}";
    }

    private static LineInput ToInput(PrintableLine line) =>
        new(line.Quantity, line.UnitPriceExclTax, line.VatRatePercent, line.DiscountPercent);

    // ---------- styles ----------

    private static void DefineStyles(Document doc)
    {
        Style normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 9;
        normal.Font.Color = Ink;

        Style title = doc.Styles.AddStyle("DocTitle", StyleNames.Normal);
        title.Font.Size = 20;
        title.Font.Bold = true;

        Style seller = doc.Styles.AddStyle("SellerName", StyleNames.Normal);
        seller.Font.Size = 13;
        seller.Font.Bold = true;

        Style muted = doc.Styles.AddStyle("Muted", StyleNames.Normal);
        muted.Font.Size = 8;
        muted.Font.Color = Muted;

        Style heading = doc.Styles.AddStyle("BlockHeading", StyleNames.Normal);
        heading.Font.Size = 8;
        heading.Font.Bold = true;
        heading.Font.Color = Muted;
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
    }

    // ---------- en-tête : émetteur, titre, client ----------

    private static void AddHeader(Section section, PrintableDocument document, CultureInfo culture)
    {
        Table header = section.AddTable();
        header.Borders.Visible = false;
        header.AddColumn(Unit.FromCentimeter(9.4));
        header.AddColumn(Unit.FromCentimeter(8));
        Row row = header.AddRow();

        Cell seller = row.Cells[0];
        seller.AddParagraph(document.Seller.Name).Style = "SellerName";
        foreach (string line in document.Seller.AddressLines)
        {
            seller.AddParagraph(line);
        }

        AddOptional(seller, culture, "Party.Phone", document.Seller.Phone);
        AddOptional(seller, culture, "Party.Email", document.Seller.Email);
        AddOptional(seller, culture, "Party.Siren", FormatSiren(document.Seller.Siren));
        AddOptional(seller, culture, "Party.VatNumber", document.Seller.VatNumber);

        Cell title = row.Cells[1];
        Paragraph titleText = title.AddParagraph(Text(culture, "Title." + document.Kind).ToUpper(culture));
        titleText.Style = "DocTitle";
        titleText.Format.Alignment = ParagraphAlignment.Right;
        Paragraph number = title.AddParagraph(document.Number is null
            ? Text(culture, "Draft.Watermark")
            : Format(culture, "Header.Number", document.Number));
        number.Format.Alignment = ParagraphAlignment.Right;
        number.Format.Font.Bold = true;
        number.Format.Font.Size = 11;
        if (document.Number is null)
        {
            Paragraph notice = title.AddParagraph(Text(culture, "Draft.Notice"));
            notice.Style = "Muted";
            notice.Format.Alignment = ParagraphAlignment.Right;
        }

        if (document.RelatedDocumentNumber is not null)
        {
            Paragraph related = title.AddParagraph(Format(culture, "Related." + document.Kind, document.RelatedDocumentNumber));
            related.Format.Alignment = ParagraphAlignment.Right;
        }

        AddDates(title, document, culture);

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(10);

        Table parties = section.AddTable();
        parties.Borders.Visible = false;
        parties.AddColumn(Unit.FromCentimeter(9.4));
        parties.AddColumn(Unit.FromCentimeter(8));
        parties.LeftPadding = Unit.FromPoint(8);
        Row partiesRow = parties.AddRow();
        partiesRow.TopPadding = Unit.FromPoint(6);
        partiesRow.BottomPadding = Unit.FromPoint(6);

        Cell delivery = partiesRow.Cells[0];
        if (document.DeliveryAddressLines.Count > 0)
        {
            delivery.AddParagraph(Text(culture, "Party.DeliveryAddress").ToUpper(culture)).Style = "BlockHeading";
            foreach (string line in document.DeliveryAddressLines)
            {
                delivery.AddParagraph(line);
            }
        }

        Cell buyer = partiesRow.Cells[1];
        buyer.Shading.Color = Band;
        buyer.AddParagraph(Text(culture, "Party.Buyer").ToUpper(culture)).Style = "BlockHeading";
        Paragraph buyerName = buyer.AddParagraph(document.Buyer.Name);
        buyerName.Format.Font.Bold = true;
        foreach (string line in document.Buyer.AddressLines)
        {
            buyer.AddParagraph(line);
        }

        AddOptional(buyer, culture, "Party.Siren", FormatSiren(document.Buyer.Siren));
        AddOptional(buyer, culture, "Party.VatNumber", document.Buyer.VatNumber);

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(12);
    }

    private static void AddDates(Cell cell, PrintableDocument document, CultureInfo culture)
    {
        Table dates = new();
        dates.Borders.Visible = false;
        dates.AddColumn(Unit.FromCentimeter(5.2)).Format.Alignment = ParagraphAlignment.Right;
        dates.AddColumn(Unit.FromCentimeter(2.6)).Format.Alignment = ParagraphAlignment.Right;

        AddDateRow(dates, culture, "Header.IssueDate", document.IssueDate);
        if (document.DeliveryDate is { } delivery && delivery != document.IssueDate)
        {
            AddDateRow(dates, culture, "Header.DeliveryDate", delivery);
        }

        if (document.DueDate is { } due)
        {
            AddDateRow(dates, culture, document.Kind == PrintableDocumentKind.Quote ? "Header.ValidUntil" : "Header.DueDate", due);
        }

        cell.Elements.Add(dates);
    }

    private static void AddDateRow(Table table, CultureInfo culture, string labelKey, DateOnly date)
    {
        Row row = table.AddRow();
        row.Cells[0].AddParagraph(Text(culture, labelKey)).Style = "Muted";
        row.Cells[1].AddParagraph(date.ToString("d", culture));
    }

    // ---------- lignes ----------

    private static void AddLines(Section section, PrintableDocument document, CultureInfo culture)
    {
        bool anyDiscount = document.Lines.Any(l => l.DiscountPercent != 0m);
        Table table = section.AddTable();
        table.Borders.Bottom.Color = Rule;
        table.Borders.Bottom.Width = Unit.FromPoint(0.5);
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);

        double designationWidth = anyDiscount ? 6.6 : 8.0;
        table.AddColumn(Unit.FromCentimeter(designationWidth));
        AddNumericColumn(table, 1.6);   // quantité
        table.AddColumn(Unit.FromCentimeter(1.6));   // unité
        AddNumericColumn(table, 2.2);   // PU HT
        if (anyDiscount)
        {
            AddNumericColumn(table, 1.4);
        }

        AddNumericColumn(table, 1.4);   // TVA
        AddNumericColumn(table, 2.2);   // total HT

        Row head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Band;
        head.Format.Font.Bold = true;
        head.Format.Font.Size = 8;
        int c = 0;
        head.Cells[c++].AddParagraph(Text(culture, "Column.Designation"));
        head.Cells[c++].AddParagraph(Text(culture, "Column.Quantity"));
        head.Cells[c++].AddParagraph(Text(culture, "Column.Unit"));
        head.Cells[c++].AddParagraph(Text(culture, "Column.UnitPrice"));
        if (anyDiscount)
        {
            head.Cells[c++].AddParagraph(Text(culture, "Column.Discount"));
        }

        head.Cells[c++].AddParagraph(Text(culture, "Column.Vat"));
        head.Cells[c].AddParagraph(Text(culture, "Column.Total"));

        foreach (PrintableLine line in document.Lines)
        {
            Row row = table.AddRow();
            c = 0;
            Paragraph designation = row.Cells[c++].AddParagraph(line.Designation);
            if (!string.IsNullOrWhiteSpace(line.Reference))
            {
                designation.AddFormattedText("  " + line.Reference, TextFormat.NotBold).Font.Color = Muted;
            }

            row.Cells[c++].AddParagraph(Quantity(line.Quantity, culture));
            row.Cells[c++].AddParagraph(line.UnitLabel);
            row.Cells[c++].AddParagraph(Money(line.UnitPriceExclTax, culture, document.Currency, unitPrice: true));
            if (anyDiscount)
            {
                row.Cells[c++].AddParagraph(line.DiscountPercent == 0m ? string.Empty : Percent(line.DiscountPercent, culture));
            }

            row.Cells[c++].AddParagraph(Percent(line.VatRatePercent, culture));
            row.Cells[c].AddParagraph(Money(LineAmounts.TotalExclTax(ToInput(line)), culture, document.Currency));
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void AddNumericColumn(Table table, double widthCm) =>
        table.AddColumn(Unit.FromCentimeter(widthCm)).Format.Alignment = ParagraphAlignment.Right;

    // ---------- totaux : ventilation par taux, puis HT, TVA, TTC, acomptes, net ----------

    private static void AddTotals(Section section, PrintableDocument document, DocumentTotals totals, CultureInfo culture)
    {
        Table layout = section.AddTable();
        layout.Borders.Visible = false;
        layout.AddColumn(Unit.FromCentimeter(9.4));
        layout.AddColumn(Unit.FromCentimeter(8));
        Row row = layout.AddRow();
        row.KeepWith = 0;

        Table breakdown = new();
        breakdown.Borders.Bottom.Color = Rule;
        breakdown.Borders.Bottom.Width = Unit.FromPoint(0.5);
        breakdown.AddColumn(Unit.FromCentimeter(2.6));
        AddNumericColumn(breakdown, 3.0);
        AddNumericColumn(breakdown, 3.0);
        Row head = breakdown.AddRow();
        head.Format.Font.Bold = true;
        head.Format.Font.Size = 8;
        head.Shading.Color = Band;
        head.Cells[0].AddParagraph(Text(culture, "Vat.Rate"));
        head.Cells[1].AddParagraph(Text(culture, "Vat.Base"));
        head.Cells[2].AddParagraph(Text(culture, "Vat.Amount"));
        foreach (VatBreakdownLine rate in totals.Breakdown)
        {
            Row r = breakdown.AddRow();
            r.Cells[0].AddParagraph(Percent(rate.VatRatePercent, culture));
            r.Cells[1].AddParagraph(Money(rate.BaseExclTax, culture, document.Currency));
            r.Cells[2].AddParagraph(Money(rate.VatAmount, culture, document.Currency));
        }

        row.Cells[0].Elements.Add(breakdown);

        Table sums = new();
        sums.Borders.Visible = false;
        sums.AddColumn(Unit.FromCentimeter(5.0));
        AddNumericColumn(sums, 3.0);
        AddSum(sums, Text(culture, "Total.ExclTax"), totals.TotalExclTax, culture, document.Currency);
        AddSum(sums, Text(culture, "Total.Vat"), totals.TotalVat, culture, document.Currency);
        Row ttc = AddSum(sums, Text(culture, "Total.InclTax"), totals.TotalInclTax, culture, document.Currency);
        decimal net = totals.TotalInclTax;
        if (document.Deductions.Count == 0)
        {
            Emphasize(ttc);
        }
        else
        {
            foreach (PrintableDeduction deduction in document.Deductions)
            {
                AddSum(sums, Format(culture, "Total.Deduction", deduction.Number, deduction.Date.ToString("d", culture)),
                    -deduction.AmountInclTax, culture, document.Currency);
                net -= deduction.AmountInclTax;
            }

            Emphasize(AddSum(sums, Text(culture, "Total.NetToPay"), net, culture, document.Currency));
        }

        row.Cells[1].Elements.Add(sums);
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(12);
    }

    private static Row AddSum(Table table, string label, decimal amount, CultureInfo culture, string currency)
    {
        Row row = table.AddRow();
        row.TopPadding = Unit.FromPoint(2);
        row.BottomPadding = Unit.FromPoint(2);
        row.Cells[0].AddParagraph(label);
        row.Cells[1].AddParagraph(Money(amount, culture, currency));
        return row;
    }

    private static void Emphasize(Row row)
    {
        row.Format.Font.Bold = true;
        row.Format.Font.Size = 11;
        row.Shading.Color = Band;
    }

    // ---------- paiement, mentions, bon pour accord ----------

    private static void AddPaymentAndMentions(Section section, PrintableDocument document, CultureInfo culture)
    {
        if (!string.IsNullOrWhiteSpace(document.PaymentTerms) || document.Iban is not null)
        {
            section.AddParagraph(Text(culture, "Payment.Terms").ToUpper(culture)).Style = "BlockHeading";
            if (!string.IsNullOrWhiteSpace(document.PaymentTerms))
            {
                section.AddParagraph(document.PaymentTerms);
            }

            if (document.Iban is not null)
            {
                string bank = Format(culture, "Payment.Iban", document.Iban);
                if (document.Bic is not null)
                {
                    bank += "  ·  " + Format(culture, "Payment.Bic", document.Bic);
                }

                section.AddParagraph(bank);
            }

            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        }

        foreach (string mention in document.LegalMentions)
        {
            section.AddParagraph(mention).Style = "Muted";
        }

        if (!string.IsNullOrWhiteSpace(document.Notes))
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
            section.AddParagraph(document.Notes);
        }

        if (document.Kind == PrintableDocumentKind.Quote)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(14);
            TextFrame frame = section.AddTextFrame();
            frame.Width = Unit.FromCentimeter(8);
            frame.Height = Unit.FromCentimeter(3);
            frame.Left = ShapePosition.Right;
            frame.LineFormat.Color = Rule;
            frame.MarginLeft = Unit.FromPoint(6);
            frame.MarginTop = Unit.FromPoint(6);
            frame.AddParagraph(Text(culture, "Quote.Acceptance")).Style = "Muted";
        }
    }

    // ---------- pied de page : identité légale de l'émetteur, pagination ----------

    private static void AddFooter(Section section, PrintableDocument document, CultureInfo culture)
    {
        PrintableParty seller = document.Seller;
        List<string> identity = [seller.Name];
        if (seller.LegalForm is not null)
        {
            identity.Add(seller.ShareCapital is null ? seller.LegalForm : $"{seller.LegalForm} {Format(culture, "Footer.ShareCapital", seller.ShareCapital)}");
        }

        if (FormatSiren(seller.Siren) is { } siren)
        {
            identity.Add(Format(culture, "Party.Siren", siren));
        }

        if (seller.Registration is not null)
        {
            identity.Add(seller.Registration);
        }

        if (seller.VatNumber is not null)
        {
            identity.Add(Format(culture, "Party.VatNumber", seller.VatNumber));
        }

        Paragraph legal = section.Footers.Primary.AddParagraph(string.Join("  ·  ", identity));
        legal.Style = "Muted";
        legal.Format.Alignment = ParagraphAlignment.Center;

        Paragraph page = section.Footers.Primary.AddParagraph();
        page.Style = "Muted";
        page.Format.Alignment = ParagraphAlignment.Center;
        page.AddText(Text(culture, "Footer.Page") + " ");
        page.AddPageField();
        page.AddText(" " + Text(culture, "Footer.Of") + " ");
        page.AddNumPagesField();
    }

    private static void AddDraftWatermark(Section section, CultureInfo culture)
    {
        TextFrame frame = section.Headers.Primary.AddTextFrame();
        frame.RelativeHorizontal = RelativeHorizontal.Page;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Left = ShapePosition.Center;
        frame.Top = ShapePosition.Center;
        frame.Width = Unit.FromCentimeter(16);
        frame.Height = Unit.FromCentimeter(4);
        frame.WrapFormat.Style = WrapStyle.None;
        Paragraph mark = frame.AddParagraph(Text(culture, "Draft.Watermark"));
        mark.Format.Alignment = ParagraphAlignment.Center;
        mark.Format.Font.Size = 64;
        mark.Format.Font.Bold = true;
        mark.Format.Font.Color = new Color(0xE5, 0xE7, 0xEB);
    }

    // ---------- formats ----------

    private static void AddOptional(Cell cell, CultureInfo culture, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            cell.AddParagraph(Format(culture, key, value)).Style = "Muted";
        }
    }

    /// <summary>SIREN groupé par trois chiffres (« 123 456 789 »), comme sur un extrait Kbis.</summary>
    internal static string? FormatSiren(string? siren) =>
        siren is { Length: 9 } && siren.All(char.IsAsciiDigit) ? $"{siren[..3]} {siren[3..6]} {siren[6..]}" : siren;

    internal static string Money(decimal amount, CultureInfo culture, string currency, bool unitPrice = false)
    {
        // Prix unitaire : jusqu'à 4 décimales (stockage), sinon centimes.
        string number = unitPrice && Rounding.ToCents(amount) != amount
            ? amount.ToString("#,##0.00##", culture)
            : Rounding.ToCents(amount).ToString("#,##0.00", culture);
        string symbol = currency == "EUR" ? "€" : currency;
        // Espace insécable : le symbole ne passe jamais seul à la ligne.
        return culture.TwoLetterISOLanguageName == "fr" ? $"{number} {symbol}" : $"{symbol}{number}";
    }

    internal static string Quantity(decimal quantity, CultureInfo culture) => quantity.ToString("#,##0.###", culture);

    internal static string Percent(decimal percent, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr" ? $"{percent.ToString("0.##", culture)} %" : $"{percent.ToString("0.##", culture)}%";

    private static string Text(CultureInfo culture, string key) =>
        Texts.GetString(key, culture) ?? throw new InvalidOperationException($"Texte d'impression manquant : {key}");

    private static string Format(CultureInfo culture, string key, params object[] args) =>
        string.Format(culture, Text(culture, key), args);
}
