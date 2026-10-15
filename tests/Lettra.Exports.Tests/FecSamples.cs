using Lettra.Calculations;
using Lettra.Domain.Billing;
using Lettra.Exports.Application;

namespace Lettra.Exports.Tests;

/// <summary>
/// Le jeu fictif du golden file : une facture à deux taux, un acompte, la facture finale qui le déduit et un avoir
/// partiel, chacun passé par les calculs de Lettra.Calculations comme à la validation.
/// </summary>
internal static class FecSamples
{
    public static readonly Guid Durand = new("01920000-0000-7000-8000-00000000a001");
    public static readonly Guid Martin = new("01920000-0000-7000-8000-00000000b002");

    public static IReadOnlyList<SalesJournalExportEntry> Year2026 { get; } =
    [
        Entry(InvoiceKind.Invoice, "FAC-000001", new DateOnly(2026, 1, 15), Durand, "Atelier Durand",
            (RevenueNature.Services, 3m, 100m, 20m), (RevenueNature.Goods, 1m, 20m, 5.5m)),
        Entry(InvoiceKind.DownPayment, "FAC-000002", new DateOnly(2026, 2, 3), Martin, "Boulangerie « Martin » & Fils",
            (RevenueNature.DownPayment, 1m, 150m, 20m)),
        Entry(InvoiceKind.Invoice, "FAC-000003", new DateOnly(2026, 3, 31), Martin, "Boulangerie « Martin » & Fils",
            (RevenueNature.Services, 5m, 100m, 20m), (RevenueNature.DownPayment, -1m, 150m, 20m)),
        Entry(InvoiceKind.CreditNote, "FAC-000004", new DateOnly(2026, 4, 2), Martin, "Boulangerie « Martin » & Fils",
            (RevenueNature.Services, 1m, 100m, 20m)),
    ];

    private static SalesJournalExportEntry Entry(
        InvoiceKind kind, string number, DateOnly date, Guid customer, string name, params (RevenueNature Nature, decimal Quantity, decimal Price, decimal Rate)[] lines)
    {
        DocumentTotals totals = DocumentCalculator.Compute(lines.Select(l => new LineInput(l.Quantity, l.Price, l.Rate)));
        IEnumerable<RevenueLine> revenue = lines.Select(l => new RevenueLine(l.Nature, LineAmounts.TotalExclTax(new LineInput(l.Quantity, l.Price, l.Rate))));
        AccountingDocumentKind accounting = kind switch
        {
            InvoiceKind.DownPayment => AccountingDocumentKind.DownPayment,
            InvoiceKind.CreditNote => AccountingDocumentKind.CreditNote,
            _ => AccountingDocumentKind.Invoice,
        };
        return new SalesJournalExportEntry(Guid.CreateVersion7(), kind, number, date, customer, name, SalesEntryCalculator.Compute(accounting, revenue, totals));
    }
}
