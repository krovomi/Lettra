using Lettra.Application.Abstractions.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Review;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Cabinet;

/// <summary>
/// Factures et acomptes validés dont l'échéance est passée et qui restent dus : ni entièrement couverts par des avoirs,
/// ni déclarés encaissés pour tout le reste à payer, ni refusés par l'acheteur. Retard au-delà de 30 jours : critique.
/// </summary>
internal sealed class OverdueInvoicesIndicatorSource(TenantDbSession session) : IDossierIndicatorSource
{
    public const int CriticalAfterDays = 30;

    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        // Montants filtrés en mémoire : SQLite ne compare pas les décimaux.
        var due = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Kind != InvoiceKind.CreditNote && i.DueDate != null && i.DueDate < today)
            .Select(i => new { i.Id, i.DueDate, i.TotalInclTax, i.CreditedInclTax })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        due = [.. due.Where(i => i.TotalInclTax - i.CreditedInclTax > 0m)];
        if (due.Count == 0)
        {
            return [new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok)];
        }

        Guid[] ids = [.. due.Select(i => i.Id.Value)];
        List<PlatformTransmissionRecord> transmissions = await db.Set<PlatformTransmissionRecord>().AsNoTracking()
            .Include(t => t.Events)
            .Where(t => ids.Contains(t.InvoiceId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        ILookup<Guid, PlatformStatusEventRecord> events = transmissions.SelectMany(t => t.Events.Select(e => (t.InvoiceId, e))).ToLookup(x => x.InvoiceId, x => x.e);

        DateOnly[] overdue =
        [
            .. due
                .Where(i =>
                {
                    IEnumerable<PlatformStatusEventRecord> history = events[i.Id.Value];
                    decimal collected = history.Where(e => e.Status == (int)PlatformStatusDto.Collected).Sum(e => e.Amount ?? 0m);
                    bool refused = history.Any(e => e.Status == (int)PlatformStatusDto.Refused);
                    return !refused && collected < i.TotalInclTax - i.CreditedInclTax;
                })
                .Select(i => i.DueDate!.Value),
        ];
        if (overdue.Length == 0)
        {
            return [new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevel.Ok)];
        }

        DateOnly oldest = overdue.Min();
        IndicatorLevel level = oldest < today.AddDays(-CriticalAfterDays) ? IndicatorLevel.Critical : IndicatorLevel.Warning;
        return [new DossierIndicator(DossierIndicatorKeys.OverdueInvoices, overdue.Length, level, oldest)];
    }
}

/// <summary>Pièces dont la dernière transmission a été rejetée par la plateforme : à corriger et retransmettre.</summary>
internal sealed class PlatformRejectionsIndicatorSource(TenantDbSession session) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<PlatformTransmissionRecord> transmissions = await db.Set<PlatformTransmissionRecord>().AsNoTracking()
            .Include(t => t.Events)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int rejected = transmissions
            .GroupBy(t => t.InvoiceId)
            .Select(g => g.OrderByDescending(t => t.SubmittedAtMs).First())
            .Count(t => t.Events.OrderByDescending(e => e.Sequence).FirstOrDefault()?.Status == (int)PlatformStatusDto.Rejected);
        return [new DossierIndicator(DossierIndicatorKeys.PlatformRejections, rejected, rejected == 0 ? IndicatorLevel.Ok : IndicatorLevel.Critical)];
    }
}

/// <summary>
/// Points de la revue du mois écoulé pas encore cochés. À faire dans la première quinzaine : en attente jusqu'au 15,
/// à surveiller ensuite.
/// </summary>
internal sealed class ReviewPendingIndicatorSource(TenantDbSession session) : IDossierIndicatorSource
{
    public const int DueDay = 15;

    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        string period = ReviewPeriod.PreviousMonth(today).ToString();
        List<string> done = await db.Set<DossierReviewMark>().AsNoTracking()
            .Where(m => m.Period == period)
            .Select(m => m.ItemKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int pending = DossierReviewChecklist.Items.Count(i => !done.Contains(i, StringComparer.Ordinal));
        DateOnly dueOn = new(today.Year, today.Month, DueDay);
        IndicatorLevel level = pending == 0 ? IndicatorLevel.Ok : today > dueOn ? IndicatorLevel.Warning : IndicatorLevel.Info;
        return [new DossierIndicator(DossierIndicatorKeys.ReviewPending, pending, level, pending == 0 ? null : dueOn)];
    }
}
