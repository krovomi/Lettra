using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les transmissions à la plateforme agréée dans la base du dossier (tables <c>PlatformTransmissions</c> et
/// <c>PlatformStatusEvents</c>). Les statuts ne s'effacent ni ne se réécrivent : un enregistrement n'ajoute que les
/// nouveaux. La dernière transmission est celle soumise en dernier (UUID v7 à égalité, trié en mémoire).
/// </summary>
internal sealed class EfPlatformTransmissionStore(TenantDbSession session) : IPlatformTransmissionStore
{
    public async Task<PlatformTransmission?> FindLatestAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .Where(r => r.InvoiceId == invoiceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        PlatformTransmissionRecord? latest = records.OrderBy(r => r.SubmittedAtMs).ThenBy(r => r.Id).LastOrDefault();
        return latest is null ? null : new PlatformTransmission(
            latest.Id,
            latest.InvoiceId,
            latest.InvoiceNumber,
            (PlatformChannelDto)latest.Channel,
            (EInvoiceFormatDto?)latest.Format,
            latest.Platform,
            latest.Reference,
            latest.TotalInclTax,
            DateTimeOffset.FromUnixTimeMilliseconds(latest.SubmittedAtMs),
            latest.Events.OrderBy(e => e.Sequence).Select(e => new PlatformStatusEvent(
                (PlatformStatusDto)e.Status, DateTimeOffset.FromUnixTimeMilliseconds(e.AtMs), e.Reason, e.Amount)));
    }

    public async Task SaveAsync(PlatformTransmission transmission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transmission);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        PlatformTransmissionRecord? record = await db.Set<PlatformTransmissionRecord>()
            .FirstOrDefaultAsync(r => r.Id == transmission.Id, cancellationToken)
            .ConfigureAwait(false);
        int known;
        if (record is null)
        {
            db.Add(new PlatformTransmissionRecord
            {
                Id = transmission.Id,
                InvoiceId = transmission.InvoiceId,
                InvoiceNumber = transmission.InvoiceNumber,
                Channel = (int)transmission.Channel,
                Format = (int?)transmission.Format,
                Platform = transmission.Platform,
                Reference = transmission.Reference,
                TotalInclTax = transmission.TotalInclTax,
                SubmittedAtMs = transmission.SubmittedAt.ToUnixTimeMilliseconds(),
            });
            known = 0;
        }
        else
        {
            known = await db.Set<PlatformStatusEventRecord>()
                .CountAsync(e => e.TransmissionId == transmission.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        for (int sequence = known; sequence < transmission.History.Count; sequence++)
        {
            PlatformStatusEvent status = transmission.History[sequence];
            db.Add(new PlatformStatusEventRecord
            {
                TransmissionId = transmission.Id,
                Sequence = sequence,
                Status = (int)status.Status,
                AtMs = status.At.ToUnixTimeMilliseconds(),
                Reason = status.Reason,
                Amount = status.Amount,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
