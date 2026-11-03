using Lettra.Exports.Application.Tax;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les achats de chaque fournisseur, lus dans les factures reçues par la plateforme agréée (table <c>ReceivedInvoices</c>) :
/// le montant HT de chaque facture rattachée à un fournisseur du dossier, négatif pour un avoir. Une facture refusée ne
/// compte pas : le fournisseur en émet une autre.
/// </summary>
internal sealed class EfSupplierPurchasesReader(TenantDbSession session) : ISupplierPurchasesReader
{
    public async Task<IReadOnlyList<SupplierPurchase>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);

        // Les montants se lisent ligne par ligne : SQLite ne fait pas la somme des décimaux.
        var rows = await db.Set<ReceivedInvoiceRecord>()
            .AsNoTracking()
            .Where(r => r.SupplierId != null && r.IssueDate >= from && r.IssueDate <= to && !r.Events.Any(e => e.Status == (int)PlatformStatus.Refused))
            .Select(r => new { SupplierId = r.SupplierId!.Value, r.IssueDate, r.Kind, r.TotalExclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. rows.Select(r => new SupplierPurchase(
                r.SupplierId,
                r.IssueDate,
                r.Kind == (int)ReceivedInvoiceKind.CreditNote ? -Math.Abs(r.TotalExclTax) : r.TotalExclTax)),
        ];
    }
}
