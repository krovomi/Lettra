using System.Globalization;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.EInvoicing;

/// <summary>Une partie (vendeur BG-4, acheteur BG-7) telle que les formats la portent.</summary>
internal sealed record EInvoiceParty(
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? PostalCode,
    string? City,
    string CountryCode,
    string? Siren,
    string? VatNumber,
    string? LegalForm);

/// <summary>Une ligne (BG-25). Un prix est toujours positif (BR-27) : une ligne en déduction porte une quantité négative.</summary>
internal sealed record EInvoiceLine(
    int Id,
    string Name,
    string? Description,
    string? SellerItemId,
    string UnitCode,
    decimal Quantity,
    decimal NetPrice,
    decimal GrossPrice,
    decimal PriceDiscount,
    decimal NetAmount,
    string VatCategory,
    decimal VatRate);

/// <summary>Une ventilation de TVA (BG-23).</summary>
internal sealed record EInvoiceVat(string Category, decimal Rate, decimal Basis, decimal Amount, string? ExemptionCode, string? ExemptionText);

/// <summary>
/// La pièce validée lue selon la norme EN 16931, commune aux syntaxes CII (Factur-X) et UBL : les deux sérialiseurs ne
/// font qu'écrire ce modèle. Les montants sont ceux de la pièce figée (calculés par <c>Lettra.Calculations</c>).
/// </summary>
internal sealed record EInvoice
{
    public const string Specification = "urn:cen.eu:en16931:2017";
    public const string ElectronicAddressScheme = "0225";
    public const string SirenScheme = "0002";
    public const string SepaCreditTransfer = "58";

    public required string TypeCode { get; init; }

    public required bool IsCreditNote { get; init; }

    public required string Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public DateOnly? DeliveryDate { get; init; }

    public string Currency { get; init; } = "EUR";

    public required IReadOnlyList<string> Notes { get; init; }

    public required EInvoiceParty Seller { get; init; }

    public required EInvoiceParty Buyer { get; init; }

    public IReadOnlyList<string> DeliveryAddressLines { get; init; } = [];

    public string? PurchaseOrderReference { get; init; }

    public string? PrecedingInvoiceNumber { get; init; }

    public string? PaymentTerms { get; init; }

    public string? Iban { get; init; }

    public string? Bic { get; init; }

    public required IReadOnlyList<EInvoiceLine> Lines { get; init; }

    public required IReadOnlyList<EInvoiceVat> Vat { get; init; }

    public required decimal TotalExclTax { get; init; }

    public required decimal TotalVat { get; init; }

    public required decimal TotalInclTax { get; init; }

    /// <summary>Une pièce hors champ (franchise, catégorie O) ne porte aucun numéro de TVA (BR-O-02).</summary>
    public bool OmitVatIdentifiers => Lines.Any(l => l.VatCategory == VatCategoryCodes.OutOfScope);

    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Date102(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>La pièce validée (numérotée, figée) et les coordonnées bancaires de la fiche entreprise.</summary>
    public static EInvoice From(InvoiceResponse invoice, CompanyProfileResponse? profile)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Number is null || invoice.IssueDate is null || invoice.Seller is null)
        {
            throw new ArgumentException("Seule une pièce validée a une facture électronique.", nameof(invoice));
        }

        VatRegime regime = (VatRegime)(int)invoice.VatRegime;
        bool credit = invoice.Kind == InvoiceKindDto.CreditNote;
        List<string> notes = [.. new[] { invoice.Subject, invoice.Notes, invoice.CreditReason }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())];
        notes.AddRange(invoice.LegalMentions.Where(m => !string.IsNullOrWhiteSpace(m)));

        return new EInvoice
        {
            TypeCode = invoice.Kind switch
            {
                InvoiceKindDto.DownPayment => "386",
                InvoiceKindDto.CreditNote => "381",
                _ => "380",
            },
            IsCreditNote = credit,
            Number = invoice.Number,
            IssueDate = invoice.IssueDate.Value,
            DueDate = credit ? null : invoice.DueDate,
            DeliveryDate = invoice.DeliveryDate,
            Notes = notes,
            Seller = Party(invoice.Seller),
            Buyer = Party(invoice.Customer),
            DeliveryAddressLines = string.IsNullOrWhiteSpace(invoice.DeliveryAddress)
                ? []
                : [.. invoice.DeliveryAddress.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            PurchaseOrderReference = invoice.PurchaseOrderReference,
            PrecedingInvoiceNumber = credit ? invoice.CreditedInvoiceNumber : null,
            PaymentTerms = credit ? null : invoice.PaymentTerms ?? profile?.PaymentTerms,
            Iban = credit ? null : profile?.Iban,
            Bic = credit ? null : profile?.Bic,
            Lines = [.. invoice.Lines.OrderBy(l => l.Position).Select(Line)],
            Vat = [.. invoice.Totals.Breakdown.Select(b => new EInvoiceVat(
                Code(b.VatCategory),
                b.VatRatePercent,
                b.BaseExclTax,
                b.VatAmount,
                b.ExemptionCode,
                VatTreatment.LegalTextOf(regime, b.ExemptionCode)))],
            TotalExclTax = invoice.Totals.TotalExclTax,
            TotalVat = invoice.Totals.TotalVat,
            TotalInclTax = invoice.Totals.TotalInclTax,
        };
    }

    private static EInvoiceParty Party(PartySnapshotResponse party) => new(
        party.Name,
        party.AddressLine1,
        party.AddressLine2,
        party.PostalCode,
        party.City,
        string.IsNullOrWhiteSpace(party.CountryCode) ? "FR" : party.CountryCode.ToUpperInvariant(),
        party.Siren,
        party.VatNumber,
        party.LegalForm);

    private static EInvoiceLine Line(DocumentLineResponse line)
    {
        // Un prix négatif (déduction) devient une quantité négative : le prix reste positif (BR-27).
        decimal sign = line.UnitPriceExclTax < 0m ? -1m : 1m;
        decimal gross = sign * line.UnitPriceExclTax;
        decimal net = Math.Round(gross * (100m - line.DiscountPercent) / 100m, 4, MidpointRounding.AwayFromZero);
        return new EInvoiceLine(
            line.Position,
            line.Designation,
            line.Description,
            line.Reference,
            line.UnitCode,
            sign * line.Quantity,
            net,
            gross,
            gross - net,
            line.TotalExclTax,
            Code(line.VatCategory),
            line.VatRatePercent);
    }

    private static string Code(VatCategoryDto category) => category switch
    {
        VatCategoryDto.Standard => VatCategoryCodes.Standard,
        VatCategoryDto.ZeroRated => VatCategoryCodes.ZeroRated,
        VatCategoryDto.Exempt => VatCategoryCodes.Exempt,
        VatCategoryDto.ReverseCharge => VatCategoryCodes.ReverseCharge,
        VatCategoryDto.IntraCommunity => VatCategoryCodes.IntraCommunity,
        VatCategoryDto.Export => VatCategoryCodes.Export,
        VatCategoryDto.OutOfScope => VatCategoryCodes.OutOfScope,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}
