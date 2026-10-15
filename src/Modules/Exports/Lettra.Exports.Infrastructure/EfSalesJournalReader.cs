using Lettra.Calculations;
using Lettra.Domain.Billing;
using Lettra.Exports.Application;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Le journal des ventes de la base du dossier (tables <c>SalesJournalEntries</c> et <c>SalesJournalLines</c>, écrites
/// à la validation), avec la nature de chaque pièce. L'ordre de validation est celui des identifiants UUID v7, trié en
/// mémoire : SQL Server ne trie pas les <c>uniqueidentifier</c> dans l'ordre de leur horodatage.
/// </summary>
internal sealed class EfSalesJournalReader(TenantDbSession session) : ISalesJournalReader
{
    // Sous la limite de 2 100 paramètres de SQL Server.
    private const int KindBatchSize = 1000;

    public async Task<IReadOnlyList<SalesJournalExportEntry>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<SalesJournalRecord> records = await db.Set<SalesJournalRecord>()
            .AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.Date >= from && e.Date <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, InvoiceKind> kinds = [];
        foreach (InvoiceId[] batch in records.Select(r => new InvoiceId(r.InvoiceId)).Chunk(KindBatchSize))
        {
            var found = await db.Set<Invoice>()
                .AsNoTracking()
                .Where(i => batch.Contains(i.Id))
                .Select(i => new { i.Id, i.Kind })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var invoice in found)
            {
                kinds[invoice.Id.Value] = invoice.Kind;
            }
        }

        return records
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Id)
            .Select(r => new SalesJournalExportEntry(
                r.InvoiceId,
                kinds.TryGetValue(r.InvoiceId, out InvoiceKind kind) ? kind : InvoiceKind.Invoice,
                r.PieceNumber,
                r.Date,
                r.CustomerId,
                r.CustomerName,
                r.Lines
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new AccountingEntryLine(l.Account, l.Debit, l.Credit, l.VatRatePercent))
                    .ToList()))
            .ToList();
    }
}
