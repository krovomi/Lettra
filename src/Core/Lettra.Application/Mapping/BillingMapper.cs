using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Pricing;

namespace Lettra.Application.Mapping;

/// <summary>
/// Passage Contracts ↔ domaine des devis, factures, fiche entreprise et grilles tarifaires, partagé par l'API et la
/// gateway in-process (ADR-0005). Les énumérations ont les mêmes valeurs des deux côtés.
/// </summary>
public static class BillingMapper
{
    public static LineDraft ToDraft(this DocumentLineRequest line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new LineDraft(
            line.CatalogItemId,
            line.Reference,
            line.Designation,
            line.Description,
            (CatalogItemKind)(int)line.Kind,
            line.UnitCode,
            line.Quantity,
            line.UnitPriceExclTax,
            line.DiscountPercent,
            line.VatRateCode,
            line.VatExemptionCode);
    }

    public static IReadOnlyList<LineDraft> ToDrafts(this IEnumerable<DocumentLineRequest>? lines) =>
        lines?.Select(l => l.ToDraft()).ToList() ?? [];

    public static InvoiceHeader ToHeader(this SaveInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new InvoiceHeader(
            request.DeliveryDate,
            request.DueDate,
            request.PaymentTerms,
            request.DeliveryAddress,
            request.PurchaseOrderReference,
            (VatRegime)(int)request.VatRegime,
            request.Subject,
            request.Notes);
    }

    public static CompanyProfile.Draft ToDraft(this SaveCompanyProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CompanyProfile.Draft(
            request.Name,
            request.LegalForm,
            request.ShareCapital,
            request.Siren,
            request.Siret,
            request.Rcs,
            request.VatNumber,
            request.AddressLine1,
            request.AddressLine2,
            request.PostalCode,
            request.City,
            request.CountryCode,
            request.VatFranchise,
            request.VatOnDebits,
            request.PaymentDays,
            request.PaymentTerms,
            request.LatePenaltyRate,
            request.EarlyPaymentDiscount,
            request.Iban,
            request.Bic,
            (VatDeclarationRegime?)(int?)request.VatDeclarationRegime);
    }

    public static IReadOnlyList<PriceListEntryDraft> ToDrafts(this IEnumerable<PriceListEntryRequest>? entries) =>
        entries?.Select(e => new PriceListEntryDraft(e.CatalogItemId, e.UnitPriceExclTax, e.DiscountPercent)).ToList() ?? [];

    public static DocumentLineResponse ToResponse(this DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new DocumentLineResponse(
            line.Position,
            line.CatalogItemId,
            line.Reference,
            line.Designation,
            line.Description,
            (CatalogItemKindDto)(int)line.Kind,
            line.UnitCode,
            line.Quantity,
            line.UnitPriceExclTax,
            line.DiscountPercent,
            line.VatRateCode,
            line.VatRatePercent,
            ToCategoryDto(line.VatCategory),
            line.VatExemptionCode,
            line.TotalExclTax);
    }

    public static DocumentTotalsResponse ToTotalsResponse<TId>(this BillingDocument<TId> document)
        where TId : struct, IEquatable<TId>
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentTotalsResponse(
            [.. document.VatSummaries.Select(v => new VatBreakdownResponse(ToCategoryDto(v.Category), v.RatePercent, v.BaseExclTax, v.VatAmount, v.ExemptionCode))],
            document.TotalExclTax,
            document.TotalVat,
            document.TotalInclTax);
    }

    public static PartySnapshotResponse ToResponse(this PartySnapshot party)
    {
        ArgumentNullException.ThrowIfNull(party);
        return new PartySnapshotResponse(
            party.Name,
            party.AddressLine1,
            party.AddressLine2,
            party.PostalCode,
            party.City,
            party.CountryCode,
            party.Siren,
            party.Siret,
            party.VatNumber,
            party.LegalForm,
            party.ShareCapital,
            party.Rcs);
    }

    public static QuoteResponse ToResponse(this Quote quote, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return new QuoteResponse(
            quote.Id.Value,
            quote.Version,
            quote.Number,
            (QuoteStatusDto)(int)quote.Status,
            quote.CustomerId.Value,
            quote.Customer.ToResponse(),
            quote.IssueDate,
            quote.ValidUntil,
            quote.IsExpired(today),
            (VatRegimeDto)(int)quote.VatRegime,
            quote.Subject,
            quote.Notes,
            [.. quote.Lines.Select(l => l.ToResponse())],
            quote.ToTotalsResponse(),
            quote.LegalMentionList,
            quote.DownPaymentsInclTax,
            quote.FinalInvoiceId?.Value,
            quote.CreatedBy,
            quote.ValidatedBy,
            quote.CreatedAt,
            quote.UpdatedAt,
            quote.ValidatedAt);
    }

    public static InvoiceResponse ToResponse(this Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new InvoiceResponse(
            invoice.Id.Value,
            invoice.Version,
            (InvoiceKindDto)(int)invoice.Kind,
            (InvoiceStatusDto)(int)invoice.Status,
            invoice.Number,
            invoice.SeriesCode,
            invoice.CustomerId.Value,
            invoice.Customer.ToResponse(),
            invoice.Seller?.ToResponse(),
            invoice.IssueDate,
            invoice.DeliveryDate,
            invoice.DueDate,
            invoice.PaymentTerms,
            invoice.DeliveryAddress,
            (VatRegimeDto)(int)invoice.VatRegime,
            (OperationCategoryDto)(int)invoice.OperationCategory,
            invoice.PurchaseOrderReference,
            invoice.Subject,
            invoice.Notes,
            invoice.QuoteId?.Value,
            invoice.QuoteNumber,
            invoice.CreditedInvoiceId?.Value,
            invoice.CreditedInvoiceNumber,
            invoice.CreditReason,
            [.. invoice.Lines.Select(l => l.ToResponse())],
            invoice.ToTotalsResponse(),
            invoice.LegalMentionList,
            invoice.CreditedInclTax,
            invoice.ContentHash,
            invoice.CreatedBy,
            invoice.ValidatedBy,
            invoice.IssuedBy,
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.ValidatedAt,
            invoice.IssuedAt,
            invoice.CreditApprovedBy,
            invoice.CreditApprovedAt,
            invoice.PaidInclTax);
    }

    public static CompanyProfileResponse ToResponse(this CompanyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new CompanyProfileResponse(
            profile.Version,
            profile.Name,
            profile.LegalForm,
            profile.ShareCapital,
            profile.Siren?.Value,
            profile.Siret?.Value,
            profile.Rcs,
            profile.VatNumber?.Value,
            profile.Address.Line1,
            profile.Address.Line2,
            profile.Address.PostalCode,
            profile.Address.City,
            profile.Address.Country.Value,
            profile.VatFranchise,
            profile.VatOnDebits,
            profile.PaymentDays,
            profile.PaymentTerms,
            profile.LatePenaltyRate,
            profile.EarlyPaymentDiscount,
            profile.Iban?.Value,
            profile.Bic,
            profile.UpdatedAt,
            (VatDeclarationRegimeDto?)(int?)profile.VatDeclarationRegime);
    }

    public static ComplianceReportResponse ToResponse(this Calculations.Compliance.ComplianceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ComplianceReportResponse(
            report.BlocksValidation,
            [.. report.Findings.Select(f => new ComplianceFindingResponse(f.Code, (ComplianceSeverityDto)(int)f.Severity, f.LegalBasis, f.Line))]);
    }

    public static VatCategoryDto ToCategoryDto(string category) => category switch
    {
        Calculations.VatCategoryCodes.Standard => VatCategoryDto.Standard,
        Calculations.VatCategoryCodes.ZeroRated => VatCategoryDto.ZeroRated,
        Calculations.VatCategoryCodes.Exempt => VatCategoryDto.Exempt,
        Calculations.VatCategoryCodes.ReverseCharge => VatCategoryDto.ReverseCharge,
        Calculations.VatCategoryCodes.IntraCommunity => VatCategoryDto.IntraCommunity,
        Calculations.VatCategoryCodes.Export => VatCategoryDto.Export,
        Calculations.VatCategoryCodes.OutOfScope => VatCategoryDto.OutOfScope,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static PriceListSummary ToSummary(this PriceList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new PriceListSummary(list.Id.Value, list.Name, list.Customers.Count, list.Entries.Count);
    }
}
