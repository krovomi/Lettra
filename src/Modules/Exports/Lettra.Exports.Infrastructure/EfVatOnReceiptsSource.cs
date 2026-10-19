using Lettra.Calculations;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// TVA sur les encaissements pour les déclarations, lue dans les encaissements du dossier : chaque part de TVA passée
/// de 44574 à 44571 (<see cref="PaymentVatShare"/>) à la date de l'encaissement, et en négatif à la date de sa
/// contre-passation. La somme suit donc exactement le mouvement du 44571 au journal de banque.
/// </summary>
internal sealed class EfVatOnReceiptsSource(TenantDbSession session) : IVatOnReceiptsSource
{
    public bool IsActive => true;

    public async Task<IReadOnlyList<VatReceiptAllocation>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Payment> payments = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.VatShares.Any() && ((p.Date >= from && p.Date <= to) || (p.ReversalDate != null && p.ReversalDate >= from && p.ReversalDate <= to)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payments.Count == 0)
        {
            return [];
        }

        List<InvoiceId> invoiceIds = [.. payments.SelectMany(p => p.VatShares).Select(s => s.InvoiceId).Distinct()];
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => invoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Kind, i.Number })
            .ToDictionaryAsync(i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        List<VatReceiptAllocation> allocations = [];
        void Add(Payment payment, DateOnly date, decimal sign)
        {
            foreach (PaymentVatShare share in payment.VatShares)
            {
                var invoice = invoices.GetValueOrDefault(share.InvoiceId);
                decimal vat = sign * share.Amount;
                decimal baseExclTax = share.VatRatePercent == 0m ? 0m : Rounding.ToCents(vat * 100m / share.VatRatePercent);
                allocations.Add(new VatReceiptAllocation(
                    share.InvoiceId.Value, invoice?.Kind ?? InvoiceKind.Invoice, invoice?.Number ?? payment.Number, date,
                    payment.CustomerId.Value, payment.CustomerName, share.VatRatePercent, baseExclTax, vat));
            }
        }

        foreach (Payment payment in payments)
        {
            if (payment.Date >= from && payment.Date <= to)
            {
                Add(payment, payment.Date, 1m);
            }

            if (payment.ReversalDate is DateOnly reversal && reversal >= from && reversal <= to)
            {
                Add(payment, reversal, -1m);
            }
        }

        return allocations;
    }

    public async Task<IReadOnlySet<Guid>> ReadDeferredAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        HashSet<Guid> deferred = [];
        if (invoiceIds.Count == 0)
        {
            return deferred;
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        foreach (Guid[] batch in invoiceIds.Chunk(500))
        {
            List<Guid> found = await db.Set<SalesJournalRecord>().AsNoTracking()
                .Where(e => batch.Contains(e.InvoiceId) && e.Lines.Any(l => l.Account == SalesAccounts.VatPending))
                .Select(e => e.InvoiceId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            deferred.UnionWith(found);
        }

        return deferred;
    }
}
