using Lettra.Domain.Billing;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Le point des créances lu dans la base du dossier : les pièces validées ou émises (sans leurs lignes) et la dernière
/// transmission de chacune, avec ses statuts. Lecture seule ; deux requêtes quelle que soit la taille du dossier.
/// </summary>
internal sealed class EfReceivablesReader(TenantDbSession session) : IReceivablesReader
{
    public async Task<IReadOnlyList<ReceivableRow>> ReadAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft)
            .Select(i => new { i.Id, i.Kind, i.Number, CustomerName = i.Customer.Name, i.DueDate, i.TotalInclTax, i.CreditedInclTax, i.PaidInclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, PlatformTransmissionRecord> latest = records
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => EfPlatformTransmissionStore.Latest(g)!);

        return
        [
            .. invoices.Select(i => new ReceivableRow(
                i.Id.Value,
                i.Kind,
                i.Number ?? string.Empty,
                i.CustomerName,
                i.DueDate,
                i.TotalInclTax,
                i.CreditedInclTax,
                latest.TryGetValue(i.Id.Value, out PlatformTransmissionRecord? record) ? EfPlatformTransmissionStore.ToTransmission(record) : null,
                i.PaidInclTax)),
        ];
    }
}
