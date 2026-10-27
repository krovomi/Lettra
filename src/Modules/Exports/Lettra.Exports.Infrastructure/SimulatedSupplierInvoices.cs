using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les factures fournisseurs que la plateforme simulée met à disposition d'un acheteur : quatre pièces fictives de trois
/// fournisseurs fictifs (une facture de fournitures, une prestation de transport, un abonnement, un avoir), en CII et en
/// UBL. Leurs références se déduisent du SIREN de l'acheteur : après un redémarrage, la plateforme rend les mêmes
/// factures et le dossier ne les reprend pas deux fois.
/// </summary>
internal static class SimulatedSupplierInvoices
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace UblInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace UblCreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private sealed record Supplier(string Name, string Siren, string VatNumber, string City);

    private sealed record Line(string Name, decimal Quantity, string Unit, decimal Price);

    private sealed record Sample(int Index, Supplier Supplier, string Number, string TypeCode, EInvoiceFormatDto Format, string BusinessProcess, int DaysAgo, int DueInDays, Line[] Lines);

    private static readonly Supplier Stationery = new("Papeterie Moreau SARL", "842157307", "FR39842157307", "Nantes");
    private static readonly Supplier Transport = new("Transports Lefèvre", "793481268", "FR87793481268", "Rennes");
    private static readonly Supplier Software = new("Logiciels Arcade SAS", "518702949", "FR36518702949", "Lyon");

    private static readonly Sample[] Samples =
    [
        new(1, Stationery, "PM-2026-0412", "380", EInvoiceFormatDto.Cii, "B1", 9, 30,
            [new("Ramettes de papier A4 80 g", 20m, "C62", 4.90m), new("Classeurs à levier", 12m, "C62", 3.25m)]),
        new(2, Transport, "TL-26-1187", "380", EInvoiceFormatDto.Ubl, "S1", 6, 45,
            [new("Livraison express Rennes - Nantes", 1m, "C62", 180m), new("Manutention", 2m, "HUR", 35m)]),
        new(3, Software, "ARC-F-55021", "380", EInvoiceFormatDto.Cii, "S1", 3, 30,
            [new("Abonnement logiciel de gestion, un mois", 1m, "C62", 89m)]),
        new(4, Stationery, "PM-2026-0430", "381", EInvoiceFormatDto.Ubl, "B1", 1, 0,
            [new("Retour de classeurs abîmés", 4m, "C62", 3.25m)]),
    ];

    /// <summary>La référence à la plateforme de la n-ième facture mise à disposition de cet acheteur.</summary>
    public static string ReferenceOf(string buyerSiren, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"PAS-R-{buyerSiren}-{index:00}");

    public static IReadOnlyList<PlatformIncomingInvoice> For(string buyerSiren, string buyerName, DateTimeOffset now)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        return [.. Samples.Select(s =>
        {
            DateOnly issued = today.AddDays(-s.DaysAgo);
            byte[] content = s.Format == EInvoiceFormatDto.Cii ? Cii(s, issued, buyerSiren, buyerName) : Ubl(s, issued, buyerSiren, buyerName);
            string extension = s.Format == EInvoiceFormatDto.Cii ? "cii" : "ubl";
            return new PlatformIncomingInvoice(
                ReferenceOf(buyerSiren, s.Index),
                s.Format,
                $"{s.Number}.{extension}.xml",
                content,
                DateTimeOffset.FromUnixTimeMilliseconds(now.AddDays(-s.DaysAgo).AddHours(1).ToUnixTimeMilliseconds()));
        })];
    }

    private static (decimal Net, decimal Vat, decimal Total) Totals(Sample sample)
    {
        decimal net = sample.Lines.Sum(l => Math.Round(l.Quantity * l.Price, 2, MidpointRounding.AwayFromZero));
        decimal vat = Math.Round(net * 0.20m, 2, MidpointRounding.AwayFromZero);
        return (net, vat, net + vat);
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static byte[] Cii(Sample sample, DateOnly issued, string buyerSiren, string buyerName)
    {
        (decimal net, decimal vat, decimal total) = Totals(sample);
        XElement Date(DateOnly date) => new(Udt + "DateTimeString", new XAttribute("format", "102"), date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        XElement Party(XName name, string partyName, string siren, string? vatNumber, string? city) =>
            new(name,
                new XElement(Ram + "Name", partyName),
                new XElement(Ram + "SpecifiedLegalOrganization", new XElement(Ram + "ID", new XAttribute("schemeID", "0002"), siren)),
                new XElement(Ram + "PostalTradeAddress", city is null ? null : new XElement(Ram + "CityName", city), new XElement(Ram + "CountryID", "FR")),
                new XElement(Ram + "URIUniversalCommunication", new XElement(Ram + "URIID", new XAttribute("schemeID", "0225"), siren)),
                vatNumber is null ? null : new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "VA"), vatNumber)));

        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Rsm + "CrossIndustryInvoice",
                new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
                new XAttribute(XNamespace.Xmlns + "ram", Ram),
                new XAttribute(XNamespace.Xmlns + "udt", Udt),
                new XElement(Rsm + "ExchangedDocumentContext",
                    new XElement(Ram + "BusinessProcessSpecifiedDocumentContextParameter", new XElement(Ram + "ID", sample.BusinessProcess)),
                    new XElement(Ram + "GuidelineSpecifiedDocumentContextParameter", new XElement(Ram + "ID", "urn:cen.eu:en16931:2017"))),
                new XElement(Rsm + "ExchangedDocument",
                    new XElement(Ram + "ID", sample.Number),
                    new XElement(Ram + "TypeCode", sample.TypeCode),
                    new XElement(Ram + "IssueDateTime", Date(issued)),
                    new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", "Facture fictive de la plateforme agréée simulée."))),
                new XElement(Rsm + "SupplyChainTradeTransaction",
                    sample.Lines.Select((l, i) => new XElement(Ram + "IncludedSupplyChainTradeLineItem",
                        new XElement(Ram + "AssociatedDocumentLineDocument", new XElement(Ram + "LineID", i + 1)),
                        new XElement(Ram + "SpecifiedTradeProduct", new XElement(Ram + "Name", l.Name)),
                        new XElement(Ram + "SpecifiedLineTradeAgreement",
                            new XElement(Ram + "NetPriceProductTradePrice", new XElement(Ram + "ChargeAmount", Number(l.Price)))),
                        new XElement(Ram + "SpecifiedLineTradeDelivery",
                            new XElement(Ram + "BilledQuantity", new XAttribute("unitCode", l.Unit), Number(l.Quantity))),
                        new XElement(Ram + "SpecifiedLineTradeSettlement",
                            new XElement(Ram + "ApplicableTradeTax",
                                new XElement(Ram + "TypeCode", "VAT"),
                                new XElement(Ram + "CategoryCode", "S"),
                                new XElement(Ram + "RateApplicablePercent", "20")),
                            new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation",
                                new XElement(Ram + "LineTotalAmount", Amount(Math.Round(l.Quantity * l.Price, 2, MidpointRounding.AwayFromZero))))))),
                    new XElement(Ram + "ApplicableHeaderTradeAgreement",
                        Party(Ram + "SellerTradeParty", sample.Supplier.Name, sample.Supplier.Siren, sample.Supplier.VatNumber, sample.Supplier.City),
                        Party(Ram + "BuyerTradeParty", buyerName, buyerSiren, null, null)),
                    new XElement(Ram + "ApplicableHeaderTradeDelivery"),
                    new XElement(Ram + "ApplicableHeaderTradeSettlement",
                        new XElement(Ram + "InvoiceCurrencyCode", "EUR"),
                        new XElement(Ram + "ApplicableTradeTax",
                            new XElement(Ram + "CalculatedAmount", Amount(vat)),
                            new XElement(Ram + "TypeCode", "VAT"),
                            new XElement(Ram + "BasisAmount", Amount(net)),
                            new XElement(Ram + "CategoryCode", "S"),
                            new XElement(Ram + "RateApplicablePercent", "20")),
                        sample.DueInDays > 0
                            ? new XElement(Ram + "SpecifiedTradePaymentTerms", new XElement(Ram + "DueDateDateTime", Date(issued.AddDays(sample.DueInDays))))
                            : null,
                        new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
                            new XElement(Ram + "LineTotalAmount", Amount(net)),
                            new XElement(Ram + "TaxBasisTotalAmount", Amount(net)),
                            new XElement(Ram + "TaxTotalAmount", new XAttribute("currencyID", "EUR"), Amount(vat)),
                            new XElement(Ram + "GrandTotalAmount", Amount(total)),
                            new XElement(Ram + "DuePayableAmount", Amount(total)))))));
        return Serialize(document);
    }

    private static byte[] Ubl(Sample sample, DateOnly issued, string buyerSiren, string buyerName)
    {
        (decimal net, decimal vat, decimal total) = Totals(sample);
        bool credit = sample.TypeCode == "381";
        XNamespace root = credit ? UblCreditNote : UblInvoice;
        XElement Money(XName name, decimal value) => new(name, new XAttribute("currencyID", "EUR"), Amount(value));
        XElement Party(string partyName, string siren, string? vatNumber, string? city) =>
            new(Cac + "Party",
                new XElement(Cbc + "EndpointID", new XAttribute("schemeID", "0225"), siren),
                new XElement(Cac + "PostalAddress", city is null ? null : new XElement(Cbc + "CityName", city), new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "FR"))),
                vatNumber is null ? null : new XElement(Cac + "PartyTaxScheme", new XElement(Cbc + "CompanyID", vatNumber), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))),
                new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", partyName), new XElement(Cbc + "CompanyID", new XAttribute("schemeID", "0002"), siren)));

        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(root + (credit ? "CreditNote" : "Invoice"),
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XElement(Cbc + "CustomizationID", "urn:cen.eu:en16931:2017"),
                new XElement(Cbc + "ProfileID", sample.BusinessProcess),
                new XElement(Cbc + "ID", sample.Number),
                new XElement(Cbc + "IssueDate", issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                !credit && sample.DueInDays > 0 ? new XElement(Cbc + "DueDate", issued.AddDays(sample.DueInDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
                new XElement(Cbc + (credit ? "CreditNoteTypeCode" : "InvoiceTypeCode"), sample.TypeCode),
                new XElement(Cbc + "Note", "Facture fictive de la plateforme agréée simulée."),
                new XElement(Cbc + "DocumentCurrencyCode", "EUR"),
                new XElement(Cac + "AccountingSupplierParty", Party(sample.Supplier.Name, sample.Supplier.Siren, sample.Supplier.VatNumber, sample.Supplier.City)),
                new XElement(Cac + "AccountingCustomerParty", Party(buyerName, buyerSiren, null, null)),
                new XElement(Cac + "TaxTotal",
                    Money(Cbc + "TaxAmount", vat),
                    new XElement(Cac + "TaxSubtotal",
                        Money(Cbc + "TaxableAmount", net),
                        Money(Cbc + "TaxAmount", vat),
                        new XElement(Cac + "TaxCategory", new XElement(Cbc + "ID", "S"), new XElement(Cbc + "Percent", "20"), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT"))))),
                new XElement(Cac + "LegalMonetaryTotal",
                    Money(Cbc + "LineExtensionAmount", net),
                    Money(Cbc + "TaxExclusiveAmount", net),
                    Money(Cbc + "TaxInclusiveAmount", total),
                    Money(Cbc + "PayableAmount", total)),
                sample.Lines.Select((l, i) => new XElement(Cac + (credit ? "CreditNoteLine" : "InvoiceLine"),
                    new XElement(Cbc + "ID", i + 1),
                    new XElement(Cbc + (credit ? "CreditedQuantity" : "InvoicedQuantity"), new XAttribute("unitCode", l.Unit), Number(l.Quantity)),
                    Money(Cbc + "LineExtensionAmount", Math.Round(l.Quantity * l.Price, 2, MidpointRounding.AwayFromZero)),
                    new XElement(Cac + "Item",
                        new XElement(Cbc + "Name", l.Name),
                        new XElement(Cac + "ClassifiedTaxCategory", new XElement(Cbc + "ID", "S"), new XElement(Cbc + "Percent", "20"), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")))),
                    new XElement(Cac + "Price", new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", "EUR"), Number(l.Price)))))));
        return Serialize(document);
    }

    private static byte[] Serialize(XDocument document)
    {
        using MemoryStream output = new();
        using (XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            document.Save(writer);
        }

        return output.ToArray();
    }
}
