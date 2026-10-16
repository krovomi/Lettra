using Lettra.Contracts.Billing;

namespace Lettra.UI.Shared.Billing;

/// <summary>Chiffre d'affaires HT d'un mois (factures et acomptes validés, moins les avoirs validés).</summary>
public sealed record MonthRevenue(DateOnly Month, decimal ExclTax);

/// <summary>
/// Les chiffres du tableau de bord, calculés à partir des listes de pièces déjà exposées (aucune requête dédiée) :
/// chiffre d'affaires HT du mois et des mois précédents, devis en attente de réponse, pièces récentes.
/// </summary>
public sealed record DashboardFigures(
    IReadOnlyList<MonthRevenue> Months,
    int InvoicesThisMonth,
    int PendingQuotes,
    decimal PendingQuotesExclTax,
    IReadOnlyList<InvoiceSummary> RecentInvoices,
    IReadOnlyList<QuoteSummary> RecentQuotes)
{
    public const int MonthCount = 6;
    public const int RecentCount = 5;

    public decimal ThisMonth => Months[^1].ExclTax;

    public decimal PreviousMonth => Months[^2].ExclTax;

    /// <summary>Évolution sur le mois précédent en pourcentage ; <c>null</c> quand le mois précédent est nul.</summary>
    public decimal? Trend => PreviousMonth == 0m ? null : Math.Round((ThisMonth - PreviousMonth) / PreviousMonth * 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>Premier jour de la période lue : le premier mois de l'histogramme.</summary>
    public static DateOnly PeriodStart(DateOnly today) => new DateOnly(today.Year, today.Month, 1).AddMonths(-(MonthCount - 1));

    public static DashboardFigures Compute(IEnumerable<InvoiceSummary> invoices, IEnumerable<QuoteSummary> quotes, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(quotes);
        DateOnly start = PeriodStart(today);
        DateOnly currentMonth = new(today.Year, today.Month, 1);
        InvoiceSummary[] issued = [.. invoices.Where(i => i is { Status: not InvoiceStatusDto.Draft, IssueDate: { } date } && date >= start && date <= today)];
        QuoteSummary[] sent = [.. quotes.Where(q => q.Status != QuoteStatusDto.Draft && q.IssueDate is not null)];

        MonthRevenue[] months =
        [
            .. Enumerable.Range(0, MonthCount).Select(offset =>
            {
                DateOnly month = start.AddMonths(offset);
                decimal total = issued
                    .Where(i => i.IssueDate!.Value.Year == month.Year && i.IssueDate.Value.Month == month.Month)
                    .Sum(i => i.Kind == InvoiceKindDto.CreditNote ? -i.TotalExclTax : i.TotalExclTax);
                return new MonthRevenue(month, total);
            }),
        ];

        QuoteSummary[] pending = [.. sent.Where(q => q is { Status: QuoteStatusDto.Validated, IsExpired: false })];
        return new DashboardFigures(
            months,
            issued.Count(i => i.Kind != InvoiceKindDto.CreditNote && i.IssueDate >= currentMonth),
            pending.Length,
            pending.Sum(q => q.TotalExclTax),
            [.. issued.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.Number, StringComparer.Ordinal).Take(RecentCount)],
            [.. sent.OrderByDescending(q => q.IssueDate).ThenByDescending(q => q.Number, StringComparer.Ordinal).Take(RecentCount)]);
    }
}
