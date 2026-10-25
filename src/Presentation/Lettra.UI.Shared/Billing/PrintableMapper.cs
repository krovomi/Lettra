using System.Globalization;
using System.Resources;
using Lettra.Contracts.Billing;
using Lettra.Printing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Des contrats au modèle d'impression (mappage écrit à la main). Les libellés (unités) sont pris dans la langue du
/// document, pas dans celle de l'utilisateur ; l'émetteur est la copie figée de la pièce validée, à défaut la fiche
/// entreprise (brouillon). Un brouillon sans date d'émission prend le jour de l'horloge injectée, jamais l'horloge système.
/// </summary>
public static class PrintableMapper
{
    private static readonly ResourceManager Texts = new(typeof(SharedResource));

    public static PrintableDocument FromQuote(QuoteResponse quote, CompanyProfileResponse? company, CultureInfo culture, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(clock);
        return new PrintableDocument
        {
            Kind = PrintableDocumentKind.Quote,
            Number = quote.Number,
            IssueDate = quote.IssueDate ?? clock.LocalToday(),
            DueDate = quote.ValidUntil,
            Seller = Seller(null, company, culture),
            Buyer = Party(quote.Customer, culture),
            Lines = Lines(quote.Lines, culture),
            LegalMentions = quote.LegalMentions,
            Notes = Join(quote.Subject, quote.Notes),
            Iban = company?.Iban,
            Bic = company?.Bic,
            Culture = culture,
        };
    }

    public static PrintableDocument FromInvoice(InvoiceResponse invoice, CompanyProfileResponse? company, CultureInfo culture, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(clock);
        return new PrintableDocument
        {
            Kind = invoice.Kind switch
            {
                InvoiceKindDto.DownPayment => PrintableDocumentKind.DownPaymentInvoice,
                InvoiceKindDto.CreditNote => PrintableDocumentKind.CreditNote,
                _ => PrintableDocumentKind.Invoice,
            },
            Number = invoice.Number,
            IssueDate = invoice.IssueDate ?? clock.LocalToday(),
            DeliveryDate = invoice.DeliveryDate,
            DueDate = invoice.DueDate,
            Seller = Seller(invoice.Seller, company, culture),
            Buyer = Party(invoice.Customer, culture),
            DeliveryAddressLines = Lines(invoice.DeliveryAddress),
            Lines = Lines(invoice.Lines, culture),
            RelatedDocumentNumber = invoice.Kind == InvoiceKindDto.CreditNote ? invoice.CreditedInvoiceNumber : invoice.QuoteNumber,
            PaymentTerms = invoice.PaymentTerms ?? company?.PaymentTerms,
            LegalMentions = invoice.LegalMentions,
            Notes = Join(invoice.Subject, invoice.Notes, invoice.CreditReason),
            Iban = company?.Iban,
            Bic = company?.Bic,
            Culture = culture,
        };
    }

    private static PrintableParty Seller(PartySnapshotResponse? snapshot, CompanyProfileResponse? company, CultureInfo culture)
    {
        if (snapshot is not null)
        {
            return Party(snapshot, culture);
        }

        if (company is null)
        {
            return new PrintableParty { Name = string.Empty };
        }

        return new PrintableParty
        {
            Name = company.Name,
            AddressLines = Address(company.AddressLine1, company.AddressLine2, company.PostalCode, company.City, company.CountryCode),
            Siren = company.Siren,
            VatNumber = company.VatNumber,
            LegalForm = company.LegalForm,
            ShareCapital = Capital(company.ShareCapital, culture),
            Registration = company.Rcs,
        };
    }

    private static PrintableParty Party(PartySnapshotResponse party, CultureInfo culture) => new()
    {
        Name = party.Name,
        AddressLines = Address(party.AddressLine1, party.AddressLine2, party.PostalCode, party.City, party.CountryCode),
        Siren = party.Siren,
        VatNumber = party.VatNumber,
        LegalForm = party.LegalForm,
        ShareCapital = Capital(party.ShareCapital, culture),
        Registration = party.Rcs,
    };

    private static List<string> Address(string line1, string? line2, string postalCode, string city, string countryCode)
    {
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(line1))
        {
            lines.Add(line1);
        }

        if (!string.IsNullOrWhiteSpace(line2))
        {
            lines.Add(line2);
        }

        string town = $"{postalCode} {city}".Trim();
        if (town.Length > 0)
        {
            lines.Add(town);
        }

        if (!string.IsNullOrWhiteSpace(countryCode) && !countryCode.Equals("FR", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(countryCode.ToUpperInvariant());
        }

        return lines;
    }

    private static List<PrintableLine> Lines(IReadOnlyList<DocumentLineResponse> lines, CultureInfo culture) =>
        [.. lines.OrderBy(l => l.Position).Select(l => new PrintableLine(l.Reference, l.Designation, l.Quantity, UnitLabel(l.UnitCode, culture),
            l.UnitPriceExclTax, l.VatRatePercent, l.DiscountPercent))];

    private static List<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>Libellé d'unité dans la langue du document, en minuscules (« heure », « jour ») ; le code sinon.</summary>
    public static string UnitLabel(string code, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        string? label = Texts.GetString("Unit." + code, culture);
        return label is null ? code : label.ToLower(culture);
    }

    private static string? Capital(decimal? amount, CultureInfo culture) =>
        amount is { } value ? value.ToString("#,##0.##", culture) + (culture.TwoLetterISOLanguageName == "fr" ? " €" : " EUR") : null;

    private static string? Join(params string?[] parts)
    {
        string[] present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }
}
