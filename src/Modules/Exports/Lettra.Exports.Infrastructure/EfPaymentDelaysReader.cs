using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Tax;
using Lettra.Exports.Domain.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les pièces validées ou émises de la base du dossier jusqu'à une date (sans leurs lignes), contestées ou non sur la
/// plateforme agréée, et les encaissements imputés. Lecture seule.
/// </summary>
internal sealed class EfPaymentDelaysReader(TenantDbSession session) : IPaymentDelaysReader
{
    public async Task<IReadOnlyList<PaymentDelayDocument>> ReadDocumentsAsync(DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.IssueDate != null && i.IssueDate <= to)
            .Select(i => new
            {
                i.Id,
                i.Kind,
                i.Number,
                CustomerName = i.Customer.Name,
                i.Customer.IsBusiness,
                i.IssueDate,
                i.DueDate,
                i.TotalInclTax,
                i.CreditedInvoiceId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> disputed =
        [
            .. records
                .GroupBy(r => r.InvoiceId)
                .Where(g => EfPlatformTransmissionStore.ToTransmission(EfPlatformTransmissionStore.Latest(g)!).Status == PlatformStatus.Disputed)
                .Select(g => g.Key),
        ];

        return
        [
            .. invoices.Select(i => new PaymentDelayDocument(
                i.Id.Value,
                i.Kind,
                i.Number ?? string.Empty,
                i.CustomerName,
                i.IsBusiness,
                i.IssueDate!.Value,
                i.DueDate,
                i.TotalInclTax,
                i.CreditedInvoiceId?.Value,
                disputed.Contains(i.Id.Value))),
        ];
    }

    public async Task<IReadOnlyList<PaymentDelaySettlement>> ReadSettlementsAsync(DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Payment> payments = await db.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.Date <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Encaissements déclarés à la plateforme (statut « encaissée », à sa date légale), pour les factures payées hors Lettra.
        IEnumerable<PaymentDelaySettlement> declared = records
            .GroupBy(r => r.InvoiceId)
            .SelectMany(g => EfPlatformTransmissionStore.ToTransmission(EfPlatformTransmissionStore.Latest(g)!).History
                .Where(e => e is { Status: PlatformStatus.Collected, Amount: > 0m })
                .Select(e => new PaymentDelaySettlement(g.Key, LegalCalendar.DateOf(e.At), e.Amount!.Value, null, Declared: true)))
            .Where(s => s.Date <= to);
        return
        [
            .. payments.SelectMany(p => p.Allocations.Select(a => new PaymentDelaySettlement(a.InvoiceId.Value, p.Date, a.Amount, p.ReversalDate))),
            .. declared,
        ];
    }
}
