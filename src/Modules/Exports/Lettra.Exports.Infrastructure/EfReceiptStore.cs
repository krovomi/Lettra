using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Exports.Application.Banking;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>Pièces justificatives (table <c>BankLineReceipts</c>) et dépenses du relevé (table <c>BankStatementLines</c>).</summary>
internal sealed class EfReceiptStore(TenantDbSession session) : IReceiptStore
{
    public async Task<IReadOnlyList<ReceiptLineResponse>> ListDebitsAsync(int max, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        // Le contenu des pièces n'est jamais lu ici : seulement leur nom, type et taille.
        var lines = await db.Set<BankStatementLine>().AsNoTracking()
            .Where(l => (double)l.Amount < 0)
            .OrderByDescending(l => l.BookingDate)
            .ThenBy(l => l.Id)
            .Take(max)
            .Select(l => new
            {
                Line = new ReceiptLineResponse(
                    l.Id, l.BookingDate, -l.Amount, l.Label, l.CounterpartyName, (ReceiptStatusDto)(int)l.ReceiptStatus,
                    l.ReceiptRequestedAt, l.ReceiptRequestedTo, l.ReceiptRequests, null, l.Version),
                Receipt = db.Set<BankLineReceipt>().Where(r => r.LineId == l.Id)
                    .Select(r => new ReceiptFileResponse(r.Id, r.FileName, r.ContentType, r.Size, r.AddedAt))
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. lines.Select(l => l.Line with { Receipt = l.Receipt })];
    }

    public async Task<IReadOnlyList<BankStatementLine>> FindLinesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<BankStatementLine>().Where(l => ids.Contains(l.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BankLineReceipt?> FindReceiptAsync(Guid lineId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<BankLineReceipt>().FirstOrDefaultAsync(r => r.LineId == lineId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveReceiptAsync(BankLineReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<BankLineReceipt> previous = await db.Set<BankLineReceipt>().Where(r => r.LineId == receipt.LineId).ToListAsync(cancellationToken).ConfigureAwait(false);
        db.Set<BankLineReceipt>().RemoveRange(previous);
        await db.Set<BankLineReceipt>().AddAsync(receipt, cancellationToken).ConfigureAwait(false);
    }
}
