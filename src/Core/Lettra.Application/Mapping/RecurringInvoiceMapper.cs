using Lettra.Application.Abstractions.Persistence;
using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Mapping;

/// <summary>Correspondances des modèles de factures récurrentes (vague 7).</summary>
public static class RecurringInvoiceMapper
{
    public static RecurringInvoiceDraft ToDraft(this SaveRecurringInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RecurringInvoiceDraft(
            request.Name,
            new CustomerId(request.CustomerId),
            (VatRegime)(int)request.VatRegime,
            request.Subject,
            request.Notes,
            request.PaymentTerms,
            request.Lines.ToDrafts(),
            (RecurrenceFrequency)(int)request.Frequency,
            (BillingTiming)(int)request.Timing,
            request.StartDate,
            request.EndDate,
            request.ValidateAutomatically,
            (PriceRevisionKind)(int)request.Revision,
            request.RevisionPercent,
            request.SyntecReferenceIndex,
            request.SyntecLatestIndex);
    }

    public static RecurringInvoiceResponse ToResponse(
        this RecurringInvoice template, string customerName, IReadOnlyDictionary<InvoiceId, RecurringInvoiceState> invoices, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(invoices);
        BillingPeriod? next = template.IsActive ? template.NextPeriod : null;
        return new RecurringInvoiceResponse(
            template.Id.Value,
            template.Version,
            template.Name,
            template.CustomerId.Value,
            customerName,
            (VatRegimeDto)(int)template.VatRegime,
            template.Subject,
            template.Notes,
            template.PaymentTerms,
            [.. template.Lines.Select(l => l.ToResponse())],
            TotalsOf(template),
            (RecurrenceFrequencyDto)(int)template.Frequency,
            (BillingTimingDto)(int)template.Timing,
            template.StartDate,
            template.EndDate,
            template.IsActive,
            template.ValidateAutomatically,
            (PriceRevisionKindDto)(int)template.Revision,
            template.RevisionPercent,
            template.SyntecReferenceIndex,
            template.SyntecLatestIndex,
            template.NextRevisionDate,
            template.RevisionAwaitsIndex,
            next?.Start,
            next?.End,
            next?.IssueOn,
            template.DuePeriod(today) is not null,
            [.. template.Occurrences.Reverse().Select(o =>
            {
                RecurringInvoiceState? state = invoices.GetValueOrDefault(o.InvoiceId);
                return new RecurringOccurrenceResponse(
                    o.PeriodStart, o.PeriodEnd, o.InvoiceId.Value, state?.Number, state is null ? null : (InvoiceStatusDto)(int)state.Status, o.RevisionCoefficient, o.CreatedAt);
            })],
            template.CreatedAt,
            template.UpdatedAt);
    }

    private static DocumentTotalsResponse TotalsOf(RecurringInvoice template)
    {
        DocumentTotals totals = DocumentCalculator.Compute(template.Lines.Select(l => l.ToInput()));
        return new DocumentTotalsResponse(
            [.. totals.Breakdown.Select(b => new VatBreakdownResponse(
                BillingMapper.ToCategoryDto(b.VatCategory),
                b.VatRatePercent,
                b.BaseExclTax,
                b.VatAmount,
                VatCategoryCodes.IsTaxed(b.VatCategory) ? null : template.Lines.First(l => l.VatCategory == b.VatCategory).VatExemptionCode))],
            totals.TotalExclTax,
            totals.TotalVat,
            totals.TotalInclTax);
    }
}
