using System.Globalization;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Domain.Billing;

namespace Lettra.Application.Features.Kpi;

/// <summary>
/// Ce qu'une facture, un acompte ou un avoir validé apporte aux indicateurs : des dates et des totaux, rien d'autre.
/// <paramref name="PaidInclTax"/> est ce qui est encaissé, par le rapprochement bancaire ou déclaré par la plateforme
/// agréée (le plus grand des deux) ; <paramref name="Refused"/> marque une facture refusée par l'acheteur, qui n'est plus
/// une créance à suivre.
/// </summary>
public sealed record InvoiceKpiFact(
    InvoiceKind Kind,
    DateOnly IssueDate,
    DateOnly? DueDate,
    Guid CustomerId,
    decimal TotalExclTax,
    decimal TotalInclTax,
    decimal CreditedInclTax,
    decimal PaidInclTax,
    bool Refused = false);

/// <summary>Ce qu'un devis validé apporte aux indicateurs.</summary>
public sealed record QuoteKpiFact(QuoteStatus Status, DateOnly IssueDate, DateOnly? ValidUntil);

/// <summary>Les pièces validées du dossier courant (les brouillons n'y sont jamais).</summary>
public sealed record BillingKpiFacts(IReadOnlyList<InvoiceKpiFact> Invoices, IReadOnlyList<QuoteKpiFact> Quotes);

/// <summary>Lit les pièces validées du dossier courant pour les indicateurs de pilotage.</summary>
public interface IBillingKpiReader
{
    Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Indicateurs de pilotage d'un dossier tirés de ses ventes : lit les pièces, calcule, publie des ratios.</summary>
public sealed class BillingKpiSource(IBillingKpiReader reader) : IDossierKpiSource
{
    public async Task<IReadOnlyList<KpiSample>> CollectAsync(DateOnly today, CancellationToken cancellationToken)
    {
        BillingKpiFacts facts = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return BillingKpiCalculator.Compute(facts, today);
    }
}

/// <summary>
/// Calcul des indicateurs de ventes d'un dossier. Les montants servent au calcul et ne sortent jamais : seuls des
/// ratios, des délais en jours, des indices et des nombres de pièces sont publiés. Un ratio sans base (aucune vente,
/// aucun devis tranché) n'est pas publié plutôt que publié à zéro : Grafana affiche « pas de donnée ».
/// </summary>
public static class BillingKpiCalculator
{
    /// <summary>Fenêtre du délai de paiement (DSO), en jours.</summary>
    public const int DsoWindowDays = 90;

    /// <summary>Un devis en attente « expire bientôt » dans ce nombre de jours.</summary>
    public const int ExpiringSoonDays = 7;

    public const int Months = 12;

    public static IReadOnlyList<KpiSample> Compute(BillingKpiFacts facts, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);
        List<KpiSample> samples = [];
        DateOnly currentMonth = new(today.Year, today.Month, 1);
        DateOnly windowStart = currentMonth.AddMonths(-(Months - 1));
        InvoiceKpiFact[] invoices = [.. facts.Invoices];
        InvoiceKpiFact[] sales = [.. invoices.Where(i => i.Kind != InvoiceKind.CreditNote)];
        InvoiceKpiFact[] receivables = [.. sales.Where(i => !i.Refused)];

        AddMonthly(samples, invoices, currentMonth);
        AddTrend(samples, invoices, currentMonth);
        AddReceivables(samples, invoices, receivables, today);
        AddCollection(samples, receivables, windowStart, today);
        AddCustomers(samples, invoices, sales, windowStart, today);
        AddQuotes(samples, facts.Quotes, windowStart, today);
        return samples;
    }

    private static void AddMonthly(List<KpiSample> samples, InvoiceKpiFact[] invoices, DateOnly currentMonth)
    {
        DateOnly[] months = [.. Enumerable.Range(0, Months).Select(i => currentMonth.AddMonths(i - (Months - 1)))];
        decimal[] revenue = [.. months.Select(m => Revenue(invoices, m, m.AddMonths(1)))];
        decimal average = revenue.Sum() / Months;
        for (int i = 0; i < Months; i++)
        {
            KpiLabel month = new(KpiMetrics.Labels.Month, months[i].ToString("yyyy-MM", CultureInfo.InvariantCulture));
            int issued = invoices.Count(f => f.Kind != InvoiceKind.CreditNote && InMonth(f.IssueDate, months[i]));
            samples.Add(new KpiSample(KpiMetrics.InvoicesIssued, issued, Label: month));
            if (average > 0m)
            {
                samples.Add(new KpiSample(KpiMetrics.RevenueIndex, Round(revenue[i] / average * 100m, 1), Label: month));
            }
        }
    }

    private static void AddTrend(List<KpiSample> samples, InvoiceKpiFact[] invoices, DateOnly currentMonth)
    {
        decimal recent = Revenue(invoices, currentMonth.AddMonths(-3), currentMonth);
        decimal before = Revenue(invoices, currentMonth.AddMonths(-6), currentMonth.AddMonths(-3));
        if (before > 0m)
        {
            samples.Add(new KpiSample(KpiMetrics.RevenueTrend, Round((recent / before) - 1m, 4)));
        }
    }

    private static void AddReceivables(List<KpiSample> samples, InvoiceKpiFact[] invoices, InvoiceKpiFact[] receivables, DateOnly today)
    {
        InvoiceKpiFact[] open = [.. receivables.Where(i => Outstanding(i) > 0m)];
        decimal outstanding = open.Sum(Outstanding);
        decimal overdue = open.Where(i => i.DueDate is { } due && due < today).Sum(Outstanding);
        samples.Add(new KpiSample(KpiMetrics.OverdueReceivables, outstanding > 0m ? Round(overdue / outstanding, 4) : 0d));

        DateOnly since = today.AddDays(-DsoWindowDays);
        decimal recentSales = invoices
            .Where(i => i.IssueDate > since && i.IssueDate <= today)
            .Sum(i => Sign(i) * i.TotalInclTax);
        if (recentSales > 0m)
        {
            samples.Add(new KpiSample(KpiMetrics.DaysSalesOutstanding, Round(outstanding / recentSales * DsoWindowDays, 1)));
        }
        else if (outstanding == 0m)
        {
            samples.Add(new KpiSample(KpiMetrics.DaysSalesOutstanding, 0d));
        }
    }

    private static void AddCollection(List<KpiSample> samples, InvoiceKpiFact[] receivables, DateOnly windowStart, DateOnly today)
    {
        InvoiceKpiFact[] issued = [.. receivables.Where(i => i.IssueDate >= windowStart && i.IssueDate <= today)];
        decimal due = issued.Sum(i => i.TotalInclTax - i.CreditedInclTax);
        if (due > 0m)
        {
            samples.Add(new KpiSample(KpiMetrics.Collection, Round(Math.Min(1m, issued.Sum(i => i.PaidInclTax) / due), 4)));
        }
    }

    private static void AddCustomers(List<KpiSample> samples, InvoiceKpiFact[] invoices, InvoiceKpiFact[] sales, DateOnly windowStart, DateOnly today)
    {
        samples.Add(new KpiSample(
            KpiMetrics.ActiveCustomers,
            sales.Where(i => i.IssueDate >= windowStart && i.IssueDate <= today).Select(i => i.CustomerId).Distinct().Count()));

        decimal[] perCustomer =
        [
            .. invoices
                .Where(i => i.IssueDate >= windowStart && i.IssueDate <= today)
                .GroupBy(i => i.CustomerId)
                .Select(g => g.Sum(i => Sign(i) * i.TotalExclTax)),
        ];
        decimal total = perCustomer.Sum();
        if (total > 0m)
        {
            samples.Add(new KpiSample(KpiMetrics.TopCustomerShare, Round(Math.Clamp(perCustomer.Max() / total, 0m, 1m), 4)));
        }
    }

    private static void AddQuotes(List<KpiSample> samples, IReadOnlyList<QuoteKpiFact> quotes, DateOnly windowStart, DateOnly today)
    {
        QuoteKpiFact[] pending = [.. quotes.Where(q => q.Status == QuoteStatus.Validated && (q.ValidUntil is null || q.ValidUntil >= today))];
        samples.Add(new KpiSample(KpiMetrics.QuotesPending, pending.Length));
        samples.Add(new KpiSample(KpiMetrics.QuotesExpiringSoon, pending.Count(q => q.ValidUntil <= today.AddDays(ExpiringSoonDays))));

        QuoteKpiFact[] recent = [.. quotes.Where(q => q.IssueDate >= windowStart && q.IssueDate <= today)];
        int accepted = recent.Count(q => q.Status is QuoteStatus.Accepted or QuoteStatus.Invoiced);
        int lost = recent.Count(q => q.Status == QuoteStatus.Refused || (q.Status == QuoteStatus.Validated && q.ValidUntil < today));
        if (accepted + lost > 0)
        {
            samples.Add(new KpiSample(KpiMetrics.QuoteConversion, Math.Round((double)accepted / (accepted + lost), 4)));
        }
    }

    private static decimal Revenue(IEnumerable<InvoiceKpiFact> invoices, DateOnly from, DateOnly to) =>
        invoices.Where(i => i.IssueDate >= from && i.IssueDate < to).Sum(i => Sign(i) * i.TotalExclTax);

    private static bool InMonth(DateOnly date, DateOnly month) => date.Year == month.Year && date.Month == month.Month;

    /// <summary>Les avoirs ont des totaux positifs : ils viennent en déduction des ventes.</summary>
    private static decimal Sign(InvoiceKpiFact invoice) => invoice.Kind == InvoiceKind.CreditNote ? -1m : 1m;

    private static decimal Outstanding(InvoiceKpiFact invoice) => invoice.TotalInclTax - invoice.CreditedInclTax - invoice.PaidInclTax;

    private static double Round(decimal value, int decimals) => (double)Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
