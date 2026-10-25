using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Les chiffres de l'accueil du dossier. Même droit que les listes de pièces dont ils sont tirés.</summary>
public sealed record GetBillingDashboardQuery(DossierId Dossier) : IQuery<Result<BillingDashboardResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Lit les pièces validées du dossier, comme les indicateurs de pilotage, et en tire l'accueil du jour légal.</summary>
internal sealed class BillingDashboardHandler(IBillingKpiReader reader, TimeProvider clock)
    : IQueryHandler<GetBillingDashboardQuery, Result<BillingDashboardResponse>>
{
    public async Task<Result<BillingDashboardResponse>> HandleAsync(GetBillingDashboardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingKpiFacts facts = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return BillingDashboardCalculator.Compute(facts, LegalCalendar.DateOf(clock.GetUtcNow()));
    }
}

/// <summary>
/// Le calcul de l'accueil : un avoir retire son montant du mois où il est émis ; un devis est en attente tant qu'il est
/// validé, sans réponse et pas encore expiré (même règle que <see cref="Quote.IsExpired"/>).
/// </summary>
public static class BillingDashboardCalculator
{
    /// <summary>Premier jour du premier mois de l'histogramme.</summary>
    public static DateOnly PeriodStart(DateOnly today) =>
        new DateOnly(today.Year, today.Month, 1).AddMonths(-(BillingDashboardResponse.MonthCount - 1));

    public static BillingDashboardResponse Compute(BillingKpiFacts facts, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);
        DateOnly start = PeriodStart(today);
        DateOnly currentMonth = new(today.Year, today.Month, 1);
        InvoiceKpiFact[] issued = [.. facts.Invoices.Where(i => i.IssueDate >= start && i.IssueDate <= today)];

        MonthRevenueDto[] months =
        [
            .. Enumerable.Range(0, BillingDashboardResponse.MonthCount).Select(offset =>
            {
                DateOnly month = start.AddMonths(offset);
                decimal total = issued
                    .Where(i => i.IssueDate.Year == month.Year && i.IssueDate.Month == month.Month)
                    .Sum(i => i.Kind == InvoiceKind.CreditNote ? -i.TotalExclTax : i.TotalExclTax);
                return new MonthRevenueDto(month, total);
            }),
        ];

        QuoteKpiFact[] pending = [.. facts.Quotes.Where(q => q.Status == QuoteStatus.Validated && !(q.ValidUntil is { } until && until < today))];
        return new BillingDashboardResponse(
            months,
            issued.Count(i => i.Kind != InvoiceKind.CreditNote && i.IssueDate >= currentMonth),
            pending.Length,
            pending.Sum(q => q.TotalExclTax));
    }
}
