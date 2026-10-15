using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lettra.Calculations;

namespace Lettra.Exports.Application.EInvoicing;

/// <summary>
/// Sérialisation UN/CEFACT CII D16B (Cross Industry Invoice), profil EN 16931 de Factur-X : c'est le XML joint au PDF
/// d'un Factur-X (<c>factur-x.xml</c>). L'ordre des éléments suit le schéma XSD ; le contenu est contrôlé en CI par les
/// règles Schematron officielles EN 16931 (build/einvoice).
/// </summary>
internal static class CiiWriter
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    public static byte[] Write(EInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Rsm + "CrossIndustryInvoice",
                new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
                new XAttribute(XNamespace.Xmlns + "qdt", Qdt),
                new XAttribute(XNamespace.Xmlns + "ram", Ram),
                new XAttribute(XNamespace.Xmlns + "udt", Udt),
                new XElement(Rsm + "ExchangedDocumentContext",
                    new XElement(Ram + "GuidelineSpecifiedDocumentContextParameter", new XElement(Ram + "ID", EInvoice.Specification))),
                new XElement(Rsm + "ExchangedDocument",
                    new XElement(Ram + "ID", invoice.Number),
                    new XElement(Ram + "TypeCode", invoice.TypeCode),
                    new XElement(Ram + "IssueDateTime", DateTime(invoice.IssueDate)),
                    invoice.Notes.Select(n => new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", n)))),
                new XElement(Rsm + "SupplyChainTradeTransaction",
                    invoice.Lines.Select(Line),
                    Agreement(invoice),
                    Delivery(invoice),
                    Settlement(invoice))));
        return Serialize(document);
    }

    internal static byte[] Serialize(XDocument document)
    {
        using MemoryStream output = new();
        using (XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            document.Save(writer);
        }

        return output.ToArray();
    }

    private static XElement DateTime(DateOnly date) =>
        new(Udt + "DateTimeString", new XAttribute("format", "102"), EInvoice.Date102(date));

    private static XElement Line(EInvoiceLine line) =>
        new(Ram + "IncludedSupplyChainTradeLineItem",
            new XElement(Ram + "AssociatedDocumentLineDocument", new XElement(Ram + "LineID", line.Id)),
            new XElement(Ram + "SpecifiedTradeProduct",
                Optional(Ram + "SellerAssignedID", line.SellerItemId),
                new XElement(Ram + "Name", line.Name),
                Optional(Ram + "Description", line.Description)),
            new XElement(Ram + "SpecifiedLineTradeAgreement",
                line.PriceDiscount == 0m
                    ? null
                    : new XElement(Ram + "GrossPriceProductTradePrice",
                        new XElement(Ram + "ChargeAmount", EInvoice.Quantity(line.GrossPrice)),
                        new XElement(Ram + "AppliedTradeAllowanceCharge",
                            new XElement(Ram + "ChargeIndicator", new XElement(Udt + "Indicator", "false")),
                            new XElement(Ram + "ActualAmount", EInvoice.Quantity(line.PriceDiscount)))),
                new XElement(Ram + "NetPriceProductTradePrice", new XElement(Ram + "ChargeAmount", EInvoice.Quantity(line.NetPrice)))),
            new XElement(Ram + "SpecifiedLineTradeDelivery",
                new XElement(Ram + "BilledQuantity", new XAttribute("unitCode", line.UnitCode), EInvoice.Quantity(line.Quantity))),
            new XElement(Ram + "SpecifiedLineTradeSettlement",
                new XElement(Ram + "ApplicableTradeTax",
                    new XElement(Ram + "TypeCode", "VAT"),
                    new XElement(Ram + "CategoryCode", line.VatCategory),
                    Rate(line.VatCategory, line.VatRate)),
                new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation",
                    new XElement(Ram + "LineTotalAmount", EInvoice.Amount(line.NetAmount)))));

    /// <summary>Le taux est porté par toutes les catégories sauf « hors champ » (BR-O-05) ; à 0 hors S (BR-E-05…).</summary>
    private static XElement? Rate(string category, decimal rate) =>
        category == VatCategoryCodes.OutOfScope
            ? null
            : new XElement(Ram + "RateApplicablePercent", EInvoice.Percent(VatCategoryCodes.IsTaxed(category) ? rate : 0m));

    private static XElement Agreement(EInvoice invoice) =>
        new(Ram + "ApplicableHeaderTradeAgreement",
            Party(Ram + "SellerTradeParty", invoice.Seller, invoice.OmitVatIdentifiers),
            Party(Ram + "BuyerTradeParty", invoice.Buyer, invoice.OmitVatIdentifiers),
            invoice.PurchaseOrderReference is { } order
                ? new XElement(Ram + "BuyerOrderReferencedDocument", new XElement(Ram + "IssuerAssignedID", order))
                : null);

    private static XElement Party(XName name, EInvoiceParty party, bool omitVat) =>
        new(name,
            new XElement(Ram + "Name", party.Name),
            party.Siren is { } siren
                ? new XElement(Ram + "SpecifiedLegalOrganization", new XElement(Ram + "ID", new XAttribute("schemeID", EInvoice.SirenScheme), siren))
                : null,
            Address(party.PostalCode, party.AddressLine1, party.AddressLine2, party.City, party.CountryCode),
            party.Siren is { } address
                ? new XElement(Ram + "URIUniversalCommunication",
                    new XElement(Ram + "URIID", new XAttribute("schemeID", EInvoice.ElectronicAddressScheme), address))
                : null,
            !omitVat && party.VatNumber is { } vat
                ? new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "VA"), vat))
                : null);

    private static XElement Address(string? postalCode, string? line1, string? line2, string? city, string country) =>
        new(Ram + "PostalTradeAddress",
            Optional(Ram + "PostcodeCode", postalCode),
            Optional(Ram + "LineOne", line1),
            Optional(Ram + "LineTwo", line2),
            Optional(Ram + "CityName", city),
            new XElement(Ram + "CountryID", country));

    private static XElement Delivery(EInvoice invoice) =>
        new(Ram + "ApplicableHeaderTradeDelivery",
            invoice.DeliveryAddressLines.Count == 0
                ? null
                : new XElement(Ram + "ShipToTradeParty",
                    new XElement(Ram + "Name", invoice.Buyer.Name),
                    new XElement(Ram + "PostalTradeAddress",
                        new XElement(Ram + "LineOne", invoice.DeliveryAddressLines[0]),
                        invoice.DeliveryAddressLines.Count > 1 ? new XElement(Ram + "LineTwo", string.Join(", ", invoice.DeliveryAddressLines.Skip(1))) : null,
                        new XElement(Ram + "CountryID", invoice.Buyer.CountryCode))),
            invoice.DeliveryDate is { } delivered
                ? new XElement(Ram + "ActualDeliverySupplyChainEvent", new XElement(Ram + "OccurrenceDateTime", DateTime(delivered)))
                : null);

    private static XElement Settlement(EInvoice invoice) =>
        new(Ram + "ApplicableHeaderTradeSettlement",
            new XElement(Ram + "InvoiceCurrencyCode", invoice.Currency),
            invoice.Iban is { } iban
                ? new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
                    new XElement(Ram + "TypeCode", EInvoice.SepaCreditTransfer),
                    new XElement(Ram + "PayeePartyCreditorFinancialAccount", new XElement(Ram + "IBANID", iban)),
                    invoice.Bic is { } bic ? new XElement(Ram + "PayeeSpecifiedCreditorFinancialInstitution", new XElement(Ram + "BICID", bic)) : null)
                : null,
            invoice.Vat.Select(v => new XElement(Ram + "ApplicableTradeTax",
                new XElement(Ram + "CalculatedAmount", EInvoice.Amount(v.Amount)),
                new XElement(Ram + "TypeCode", "VAT"),
                VatCategoryCodes.IsTaxed(v.Category) ? null : Optional(Ram + "ExemptionReason", v.ExemptionText),
                new XElement(Ram + "BasisAmount", EInvoice.Amount(v.Basis)),
                new XElement(Ram + "CategoryCode", v.Category),
                VatCategoryCodes.IsTaxed(v.Category) ? null : Optional(Ram + "ExemptionReasonCode", v.ExemptionCode),
                Rate(v.Category, v.Rate))),
            invoice.PaymentTerms is null && invoice.DueDate is null
                ? null
                : new XElement(Ram + "SpecifiedTradePaymentTerms",
                    Optional(Ram + "Description", invoice.PaymentTerms),
                    invoice.DueDate is { } due ? new XElement(Ram + "DueDateDateTime", DateTime(due)) : null),
            new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
                new XElement(Ram + "LineTotalAmount", EInvoice.Amount(invoice.Lines.Sum(l => l.NetAmount))),
                new XElement(Ram + "TaxBasisTotalAmount", EInvoice.Amount(invoice.TotalExclTax)),
                new XElement(Ram + "TaxTotalAmount", new XAttribute("currencyID", invoice.Currency), EInvoice.Amount(invoice.TotalVat)),
                new XElement(Ram + "GrandTotalAmount", EInvoice.Amount(invoice.TotalInclTax)),
                new XElement(Ram + "DuePayableAmount", EInvoice.Amount(invoice.TotalInclTax))),
            invoice.PrecedingInvoiceNumber is { } preceding
                ? new XElement(Ram + "InvoiceReferencedDocument", new XElement(Ram + "IssuerAssignedID", preceding))
                : null);

    private static XElement? Optional(XName name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value.Trim());
}
