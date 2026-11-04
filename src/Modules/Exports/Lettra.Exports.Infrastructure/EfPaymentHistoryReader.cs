using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Reminders;
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
/// (statut « encaissée », daté du jour de Paris de la déclaration). Sous un échéancier en cours, la facture est
/// attendue à sa prochaine échéance pas encore couverte, et soldée à temps si elle l'est à la dernière : un client qui
/// tient son échéancier n'est pas compté en retard.
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
        Dictionary<InvoiceId, PaymentPlan> plans = (await db.Set<PaymentPlan>()
            .AsNoTracking()
            .Where(p => p.Status == PaymentPlanStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.RequestedAt).First());

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
                decimal outstanding = Math.Max(0m, due - paid);
                DateOnly? expected = !plans.TryGetValue(i.Id, out PaymentPlan? plan) ? i.DueDate
                    : outstanding > 0m ? plan.NextDueDate(outstanding)
                    : plan.FinalDueDate;
                return new PaymentHistoryInvoice(
                    i.Id.Value, i.CustomerId.Value, i.CustomerName, i.Number!, i.IssueDate, expected, due, outstanding, entries);
            }),
        ];
    }
}
