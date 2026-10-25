using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Domain.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// L'historique de paiement lu dans la base du dossier, en trois requêtes quelle que soit sa taille : factures et
/// acomptes validés (sans leurs lignes), encaissements non contre-passés avec leurs imputations, et transmissions à la
/// plateforme. Une pièce prend ses encaissements saisis dans Lettra ; sans eux, ceux déclarés à la plateforme
/// (statut « encaissée », daté du jour de Paris de la déclaration).
/// </summary>
internal sealed class EfPaymentHistoryReader(TenantDbSession session) : IPaymentHistoryReader
{
    public async Task<IReadOnlyList<PaymentHistoryInvoice>> ReadAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Number != null && i.Kind != InvoiceKind.CreditNote)
            .Select(i => new
            {
                i.Id,
                i.CustomerId,
                CustomerName = i.Customer.Name,
                i.Number,
                i.IssueDate,
                i.DueDate,
                i.TotalInclTax,
                i.CreditedInclTax,
                i.PaidInclTax,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<Payment> payments = await db.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Posted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ILookup<InvoiceId, PaymentHistoryEntry> recorded = payments
            .SelectMany(p => p.Allocations.Select(a => (a.InvoiceId, Entry: new PaymentHistoryEntry(p.Date, a.Amount))))
            .ToLookup(a => a.InvoiceId, a => a.Entry);
        Dictionary<Guid, PaymentHistoryEntry[]> declared = records
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(
                g => g.Key,
                g => EfPlatformTransmissionStore.ToTransmission(EfPlatformTransmissionStore.Latest(g)!).History
                    .Where(e => e is { Status: PlatformStatus.Collected, Amount: > 0m })
                    .Select(e => new PaymentHistoryEntry(LegalCalendar.DateOf(e.At), e.Amount!.Value))
                    .ToArray());

        return
        [
            .. invoices.Select(i =>
            {
                PaymentHistoryEntry[] entries = recorded.Contains(i.Id)
                    ? [.. recorded[i.Id]]
                    : declared.GetValueOrDefault(i.Id.Value) ?? [];
                decimal due = i.TotalInclTax - i.CreditedInclTax;
                decimal paid = Math.Max(i.PaidInclTax, entries.Sum(e => e.Amount));
                return new PaymentHistoryInvoice(
                    i.Id.Value, i.CustomerId.Value, i.CustomerName, i.Number!, i.IssueDate, i.DueDate, due, Math.Max(0m, due - paid), entries);
            }),
        ];
    }
}
