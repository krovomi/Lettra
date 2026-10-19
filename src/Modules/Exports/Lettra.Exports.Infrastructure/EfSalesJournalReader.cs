using Lettra.Calculations;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Exports.Application;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Banking;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Le journal des ventes de la base du dossier (tables <c>SalesJournalEntries</c> et <c>SalesJournalLines</c>, écrites
/// à la validation), avec la nature de chaque pièce, et le journal de banque (<c>BankJournalEntries</c>, vague 6), avec
/// le code de lettrage actif de chaque écriture. L'ordre de validation est celui des identifiants UUID v7, trié en
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

        List<BankJournalRecord> bank = await db.Set<BankJournalRecord>()
            .AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.Date >= from && e.Date <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        List<Lettering> letterings = bank.Count == 0 && records.Count == 0
            ? []
            : await db.Set<Lettering>().AsNoTracking().Where(l => l.CancelledAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<LetteringItem, Lettering> lettered = [];
        foreach (Lettering lettering in letterings)
        {
            foreach (LetteringItem item in lettering.Items)
            {
                lettered[item] = lettering;
            }
        }

        static List<AccountingEntryLine> Lines<T>(IEnumerable<T> lines, Func<T, int> number, Func<T, AccountingEntryLine> map) =>
            [.. lines.OrderBy(number).Select(map)];

        IEnumerable<(DateOnly Date, Guid Id, SalesJournalExportEntry Entry)> sales = records.Select(r =>
        {
            lettered.TryGetValue(new LetteringItem(LetteredEntryKind.Invoice, r.InvoiceId), out Lettering? lettering);
            return (r.Date, r.Id, new SalesJournalExportEntry(
                r.InvoiceId,
                kinds.TryGetValue(r.InvoiceId, out InvoiceKind kind) ? kind : InvoiceKind.Invoice,
                r.PieceNumber,
                r.Date,
                r.CustomerId,
                r.CustomerName,
                Lines(r.Lines, l => l.LineNumber, l => new AccountingEntryLine(l.Account, l.Debit, l.Credit, l.VatRatePercent)),
                SalesJournalExportEntry.SalesJournal,
                lettering?.Code,
                lettering?.Date));
        });
        IEnumerable<(DateOnly Date, Guid Id, SalesJournalExportEntry Entry)> payments = bank.Select(r =>
        {
            lettered.TryGetValue(new LetteringItem(r.IsReversal ? LetteredEntryKind.PaymentReversal : LetteredEntryKind.Payment, r.PaymentId), out Lettering? lettering);
            return (r.Date, r.Id, new SalesJournalExportEntry(
                r.PaymentId,
                InvoiceKind.Invoice,
                r.PieceNumber,
                r.Date,
                r.CustomerId,
                r.CustomerName,
                Lines(r.Lines, l => l.LineNumber, l => new AccountingEntryLine(l.Account, l.Debit, l.Credit, l.VatRatePercent)),
                PaymentAccounts.JournalCode,
                lettering?.Code,
                lettering?.Date,
                r.IsReversal));
        });

        return [.. sales.Concat(payments).OrderBy(e => e.Date).ThenBy(e => e.Id).Select(e => e.Entry)];
    }
}
