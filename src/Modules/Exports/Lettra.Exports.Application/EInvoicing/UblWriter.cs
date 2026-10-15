using System.Xml.Linq;
using Lettra.Calculations;

namespace Lettra.Exports.Application.EInvoicing;

/// <summary>
/// Sérialisation OASIS UBL 2.1 conforme à EN 16931 : <c>Invoice</c> pour une facture ou un acompte, <c>CreditNote</c>
/// pour un avoir. L'ordre des éléments suit le schéma XSD UBL 2.1 ; le contenu est contrôlé en CI par les règles
/// Schematron officielles EN 16931 (build/einvoice).
/// </summary>
internal static class UblWriter
{
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace CreditNoteNs = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public static byte[] Write(EInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        bool credit = invoice.IsCreditNote;
        XNamespace root = credit ? CreditNoteNs : InvoiceNs;
        XDocument document = new(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(root + (credit ? "CreditNote" : "Invoice"),
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XElement(Cbc + "CustomizationID", EInvoice.Specification),
                new XElement(Cbc + "ID", invoice.Number),
                new XElement(Cbc + "IssueDate", EInvoice.IsoDate(invoice.IssueDate)),
                !credit && invoice.DueDate is { } due ? new XElement(Cbc + "DueDate", EInvoice.IsoDate(due)) : null,
                new XElement(Cbc + (credit ? "CreditNoteTypeCode" : "InvoiceTypeCode"), invoice.TypeCode),
                // En UBL, le code sujet précède le texte : « #PMT#… » (convention EN 16931 reprise par la XP Z12-012).
                invoice.Notes.Select(n => new XElement(Cbc + "Note", n.SubjectCode is { } code ? $"#{code}#{n.Content}" : n.Content)),
                new XElement(Cbc + "DocumentCurrencyCode", invoice.Currency),
                invoice.PurchaseOrderReference is { } order ? new XElement(Cac + "OrderReference", new XElement(Cbc + "ID", order)) : null,
                invoice.PrecedingInvoiceNumber is { } preceding
                    ? new XElement(Cac + "BillingReference", new XElement(Cac + "InvoiceDocumentReference",
                        new XElement(Cbc + "ID", preceding),
                        invoice.PrecedingInvoiceDate is { } issued ? new XElement(Cbc + "IssueDate", EInvoice.IsoDate(issued)) : null))
                    : null,
                new XElement(Cac + "AccountingSupplierParty", Party(invoice.Seller, invoice.OmitVatIdentifiers)),
                new XElement(Cac + "AccountingCustomerParty", Party(invoice.Buyer, invoice.OmitVatIdentifiers)),
                Delivery(invoice),
                invoice.Iban is { } iban
                    ? new XElement(Cac + "PaymentMeans",
                        new XElement(Cbc + "PaymentMeansCode", EInvoice.SepaCreditTransfer),
                        new XElement(Cac + "PayeeFinancialAccount",
                            new XElement(Cbc + "ID", iban),
                            invoice.Bic is { } bic ? new XElement(Cac + "FinancialInstitutionBranch", new XElement(Cbc + "ID", bic)) : null))
                    : null,
                invoice.PaymentTerms is { } terms ? new XElement(Cac + "PaymentTerms", new XElement(Cbc + "Note", terms)) : null,
                new XElement(Cac + "TaxTotal",
                    Money(Cbc + "TaxAmount", invoice.TotalVat, invoice.Currency),
                    invoice.Vat.Select(v => new XElement(Cac + "TaxSubtotal",
                        Money(Cbc + "TaxableAmount", v.Basis, invoice.Currency),
                        Money(Cbc + "TaxAmount", v.Amount, invoice.Currency),
                        Category(Cac + "TaxCategory", v.Category, v.Rate, v.ExemptionCode, v.ExemptionText)))),
                new XElement(Cac + "LegalMonetaryTotal",
                    Money(Cbc + "LineExtensionAmount", invoice.Lines.Sum(l => l.NetAmount), invoice.Currency),
                    Money(Cbc + "TaxExclusiveAmount", invoice.TotalExclTax, invoice.Currency),
                    Money(Cbc + "TaxInclusiveAmount", invoice.TotalInclTax, invoice.Currency),
                    Money(Cbc + "PayableAmount", invoice.TotalInclTax, invoice.Currency)),
                invoice.Lines.Select(l => Line(l, credit, invoice.Currency))));
        return CiiWriter.Serialize(document);
    }

    private static XElement Money(XName name, decimal amount, string currency) =>
        new(name, new XAttribute("currencyID", currency), EInvoice.Amount(amount));

    private static XElement Party(EInvoiceParty party, bool omitVat) =>
        new(Cac + "Party",
            party.Siren is { } address
                ? new XElement(Cbc + "EndpointID", new XAttribute("schemeID", EInvoice.ElectronicAddressScheme), address)
                : null,
            new XElement(Cac + "PostalAddress",
                Optional(Cbc + "StreetName", party.AddressLine1),
                Optional(Cbc + "AdditionalStreetName", party.AddressLine2),
                Optional(Cbc + "CityName", party.City),
                Optional(Cbc + "PostalZone", party.PostalCode),
                new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", party.CountryCode))),
            !omitVat && party.VatNumber is { } vat
                ? new XElement(Cac + "PartyTaxScheme", new XElement(Cbc + "CompanyID", vat), new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")))
                : null,
            new XElement(Cac + "PartyLegalEntity",
                new XElement(Cbc + "RegistrationName", party.Name),
                party.Siren is { } siren ? new XElement(Cbc + "CompanyID", new XAttribute("schemeID", EInvoice.SirenScheme), siren) : null,
                Optional(Cbc + "CompanyLegalForm", party.LegalForm)));

    private static XElement? Delivery(EInvoice invoice) =>
        invoice.DeliveryDate is null && invoice.DeliveryAddressLines.Count == 0
            ? null
            : new XElement(Cac + "Delivery",
                invoice.DeliveryDate is { } delivered ? new XElement(Cbc + "ActualDeliveryDate", EInvoice.IsoDate(delivered)) : null,
                invoice.DeliveryAddressLines.Count == 0
                    ? null
                    : new XElement(Cac + "DeliveryLocation",
                        new XElement(Cac + "Address",
                            new XElement(Cbc + "StreetName", invoice.DeliveryAddressLines[0]),
                            invoice.DeliveryAddressLines.Count > 1
                                ? new XElement(Cbc + "AdditionalStreetName", string.Join(", ", invoice.DeliveryAddressLines.Skip(1)))
                                : null,
                            new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", invoice.Buyer.CountryCode)))));

    private static XElement Category(XName name, string category, decimal rate, string? exemptionCode, string? exemptionText) =>
        new(name,
            new XElement(Cbc + "ID", category),
            category == VatCategoryCodes.OutOfScope
                ? null
                : new XElement(Cbc + "Percent", EInvoice.Percent(VatCategoryCodes.IsTaxed(category) ? rate : 0m)),
            VatCategoryCodes.IsTaxed(category) ? null : Optional(Cbc + "TaxExemptionReasonCode", exemptionCode),
            VatCategoryCodes.IsTaxed(category) ? null : Optional(Cbc + "TaxExemptionReason", exemptionText),
            new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")));

    private static XElement Line(EInvoiceLine line, bool credit, string currency) =>
        new(Cac + (credit ? "CreditNoteLine" : "InvoiceLine"),
            new XElement(Cbc + "ID", line.Id),
            new XElement(Cbc + (credit ? "CreditedQuantity" : "InvoicedQuantity"), new XAttribute("unitCode", line.UnitCode), EInvoice.Quantity(line.Quantity)),
            Money(Cbc + "LineExtensionAmount", line.NetAmount, currency),
            new XElement(Cac + "Item",
                Optional(Cbc + "Description", line.Description),
                new XElement(Cbc + "Name", line.Name),
                line.SellerItemId is { Length: > 0 } id ? new XElement(Cac + "SellersItemIdentification", new XElement(Cbc + "ID", id)) : null,
                new XElement(Cac + "ClassifiedTaxCategory",
                    new XElement(Cbc + "ID", line.VatCategory),
                    line.VatCategory == VatCategoryCodes.OutOfScope
                        ? null
                        : new XElement(Cbc + "Percent", EInvoice.Percent(VatCategoryCodes.IsTaxed(line.VatCategory) ? line.VatRate : 0m)),
                    new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")))),
            new XElement(Cac + "Price",
                new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", currency), EInvoice.Quantity(line.NetPrice)),
                line.PriceDiscount == 0m
                    ? null
                    : new XElement(Cac + "AllowanceCharge",
                        new XElement(Cbc + "ChargeIndicator", "false"),
                        new XElement(Cbc + "Amount", new XAttribute("currencyID", currency), EInvoice.Quantity(line.PriceDiscount)),
                        new XElement(Cbc + "BaseAmount", new XAttribute("currencyID", currency), EInvoice.Quantity(line.GrossPrice)))));

    private static XElement? Optional(XName name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value.Trim());
}
