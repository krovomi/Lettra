using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lettra.Contracts.Exports;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;

namespace Lettra.Exports.Application.EInvoicing.Reception;

/// <summary>Une facture électronique reçue, relue : l'en-tête, les lignes, la TVA et les notes.</summary>
internal sealed record ReadEInvoice(
    ReceivedInvoiceHeader Header,
    IReadOnlyList<ReceivedInvoiceLineResponse> Lines,
    IReadOnlyList<ReceivedInvoiceVatResponse> Vat,
    IReadOnlyList<string> Notes);

/// <summary>
/// Lecture d'une facture électronique EN 16931 reçue d'un fournisseur, en CII (le XML d'un Factur-X) ou en UBL 2.1
/// (Invoice ou CreditNote). La plateforme a déjà contrôlé le fichier : la lecture ne retient que ce que Lettra affiche et
/// refuse un fichier sans numéro, sans date ou sans totaux. Les DTD et entités externes sont refusées.
/// </summary>
internal static class EInvoiceReader
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace UblInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace UblCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>La facture lue, ou <c>null</c> si le fichier n'est pas une facture EN 16931 lisible.</summary>
    public static ReadEInvoice? Read(EInvoiceFormat format, ReadOnlyMemory<byte> content)
    {
        XDocument? xml = Load(content);
        if (xml?.Root is not { } root)
        {
            return null;
        }

        try
        {
            return format switch
            {
                EInvoiceFormat.Cii when root.Name == Rsm + "CrossIndustryInvoice" => ReadCii(root),
                EInvoiceFormat.Ubl when root.Name == UblInvoice + "Invoice" || root.Name == UblCreditNote + "CreditNote" => ReadUbl(root),
                _ => null,
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static XDocument? Load(ReadOnlyMemory<byte> content)
    {
        try
        {
            using MemoryStream stream = new(content.ToArray());
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static ReadEInvoice? ReadCii(XElement root)
    {
        XElement? document = root.Element(Rsm + "ExchangedDocument");
        XElement? transaction = root.Element(Rsm + "SupplyChainTradeTransaction");
        XElement? agreement = transaction?.Element(Ram + "ApplicableHeaderTradeAgreement");
        XElement? settlement = transaction?.Element(Ram + "ApplicableHeaderTradeSettlement");
        XElement? seller = agreement?.Element(Ram + "SellerTradeParty");
        XElement? buyer = agreement?.Element(Ram + "BuyerTradeParty");
        XElement? totals = settlement?.Element(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation");
        string? number = Text(document?.Element(Ram + "ID"));
        DateOnly? issued = Date102(document?.Element(Ram + "IssueDateTime")?.Element(Udt + "DateTimeString"));
        string? sellerName = Text(seller?.Element(Ram + "Name"));
        string? buyerSiren = Text(buyer?.Element(Ram + "SpecifiedLegalOrganization")?.Element(Ram + "ID"));
        if (number is null || issued is null || sellerName is null || buyerSiren is null || totals is null)
        {
            return null;
        }

        ReceivedInvoiceHeader header = new(
            KindOf(Text(document!.Element(Ram + "TypeCode"))),
            number,
            issued.Value,
            Date102(settlement!.Element(Ram + "SpecifiedTradePaymentTerms")?.Element(Ram + "DueDateDateTime")?.Element(Udt + "DateTimeString")),
            sellerName,
            Text(seller!.Element(Ram + "SpecifiedLegalOrganization")?.Element(Ram + "ID")),
            Text(seller.Element(Ram + "SpecifiedTaxRegistration")?.Element(Ram + "ID")),
            buyerSiren,
            Text(settlement.Element(Ram + "InvoiceCurrencyCode")) ?? "EUR",
            Amount(totals.Element(Ram + "TaxBasisTotalAmount")),
            totals.Elements(Ram + "TaxTotalAmount").Select(Amount).FirstOrDefault(),
            Amount(totals.Element(Ram + "GrandTotalAmount")),
            Text(root.Element(Rsm + "ExchangedDocumentContext")?.Element(Ram + "BusinessProcessSpecifiedDocumentContextParameter")?.Element(Ram + "ID")));

        ReceivedInvoiceLineResponse[] lines = [.. transaction!.Elements(Ram + "IncludedSupplyChainTradeLineItem").Select(l =>
        {
            XElement? tax = l.Element(Ram + "SpecifiedLineTradeSettlement")?.Element(Ram + "ApplicableTradeTax");
            XElement? quantity = l.Element(Ram + "SpecifiedLineTradeDelivery")?.Element(Ram + "BilledQuantity");
            return new ReceivedInvoiceLineResponse(
                Text(l.Element(Ram + "SpecifiedTradeProduct")?.Element(Ram + "Name")) ?? string.Empty,
                Amount(quantity),
                quantity?.Attribute("unitCode")?.Value,
                Amount(l.Element(Ram + "SpecifiedLineTradeAgreement")?.Element(Ram + "NetPriceProductTradePrice")?.Element(Ram + "ChargeAmount")),
                Amount(l.Element(Ram + "SpecifiedLineTradeSettlement")?.Element(Ram + "SpecifiedTradeSettlementLineMonetarySummation")?.Element(Ram + "LineTotalAmount")),
                Text(tax?.Element(Ram + "CategoryCode")) ?? string.Empty,
                OptionalAmount(tax?.Element(Ram + "RateApplicablePercent")));
        })];
        ReceivedInvoiceVatResponse[] vat = [.. settlement.Elements(Ram + "ApplicableTradeTax").Select(t => new ReceivedInvoiceVatResponse(
            Text(t.Element(Ram + "CategoryCode")) ?? string.Empty,
            OptionalAmount(t.Element(Ram + "RateApplicablePercent")),
            Amount(t.Element(Ram + "BasisAmount")),
            Amount(t.Element(Ram + "CalculatedAmount"))))];
        string[] notes = [.. document.Elements(Ram + "IncludedNote").Select(n => Text(n.Element(Ram + "Content"))).OfType<string>()];
        return new ReadEInvoice(header, lines, vat, notes);
    }

    private static ReadEInvoice? ReadUbl(XElement root)
    {
        bool credit = root.Name == UblCreditNote + "CreditNote";
        XElement? seller = root.Element(Cac + "AccountingSupplierParty")?.Element(Cac + "Party");
        XElement? buyer = root.Element(Cac + "AccountingCustomerParty")?.Element(Cac + "Party");
        XElement? totals = root.Element(Cac + "LegalMonetaryTotal");
        XElement? taxTotal = root.Element(Cac + "TaxTotal");
        string? number = Text(root.Element(Cbc + "ID"));
        DateOnly? issued = IsoDate(root.Element(Cbc + "IssueDate"));
        string? sellerName = Text(seller?.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "RegistrationName"))
            ?? Text(seller?.Element(Cac + "PartyName")?.Element(Cbc + "Name"));
        string? buyerSiren = Text(buyer?.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "CompanyID"));
        if (number is null || issued is null || sellerName is null || buyerSiren is null || totals is null)
        {
            return null;
        }

        ReceivedInvoiceHeader header = new(
            credit ? ReceivedInvoiceKind.CreditNote : KindOf(Text(root.Element(Cbc + "InvoiceTypeCode"))),
            number,
            issued.Value,
            IsoDate(root.Element(Cbc + "DueDate")),
            sellerName,
            Text(seller!.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "CompanyID")),
            Text(seller.Element(Cac + "PartyTaxScheme")?.Element(Cbc + "CompanyID")),
            buyerSiren,
            Text(root.Element(Cbc + "DocumentCurrencyCode")) ?? "EUR",
            Amount(totals.Element(Cbc + "TaxExclusiveAmount")),
            Amount(taxTotal?.Element(Cbc + "TaxAmount")),
            Amount(totals.Element(Cbc + "TaxInclusiveAmount")),
            Text(root.Element(Cbc + "ProfileID")));

        string lineName = credit ? "CreditNoteLine" : "InvoiceLine";
        string quantityName = credit ? "CreditedQuantity" : "InvoicedQuantity";
        ReceivedInvoiceLineResponse[] lines = [.. root.Elements(Cac + lineName).Select(l =>
        {
            XElement? item = l.Element(Cac + "Item");
            XElement? tax = item?.Element(Cac + "ClassifiedTaxCategory");
            XElement? quantity = l.Element(Cbc + quantityName);
            return new ReceivedInvoiceLineResponse(
                Text(item?.Element(Cbc + "Name")) ?? string.Empty,
                Amount(quantity),
                quantity?.Attribute("unitCode")?.Value,
                Amount(l.Element(Cac + "Price")?.Element(Cbc + "PriceAmount")),
                Amount(l.Element(Cbc + "LineExtensionAmount")),
                Text(tax?.Element(Cbc + "ID")) ?? string.Empty,
                OptionalAmount(tax?.Element(Cbc + "Percent")));
        })];
        ReceivedInvoiceVatResponse[] vat = [.. (taxTotal?.Elements(Cac + "TaxSubtotal") ?? []).Select(t => new ReceivedInvoiceVatResponse(
            Text(t.Element(Cac + "TaxCategory")?.Element(Cbc + "ID")) ?? string.Empty,
            OptionalAmount(t.Element(Cac + "TaxCategory")?.Element(Cbc + "Percent")),
            Amount(t.Element(Cbc + "TaxableAmount")),
            Amount(t.Element(Cbc + "TaxAmount"))))];
        string[] notes = [.. root.Elements(Cbc + "Note").Select(Text).OfType<string>()];
        return new ReadEInvoice(header, lines, vat, notes);
    }

    private static ReceivedInvoiceKind KindOf(string? typeCode) => typeCode switch
    {
        "381" => ReceivedInvoiceKind.CreditNote,
        "386" => ReceivedInvoiceKind.DownPayment,
        _ => ReceivedInvoiceKind.Invoice,
    };

    private static string? Text(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static decimal Amount(XElement? element) => OptionalAmount(element) ?? 0m;

    /// <exception cref="FormatException">Montant illisible : le fichier est refusé.</exception>
    private static decimal? OptionalAmount(XElement? element) =>
        Text(element) is { } text ? decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture) : null;

    private static DateOnly? Date102(XElement? element) =>
        Text(element) is { } text ? DateOnly.ParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture) : null;

    private static DateOnly? IsoDate(XElement? element) =>
        Text(element) is { } text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
