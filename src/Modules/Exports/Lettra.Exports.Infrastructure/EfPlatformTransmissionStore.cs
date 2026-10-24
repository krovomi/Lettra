using Lettra.Application.Abstractions.Integrity;
using Lettra.Domain.Integrity;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;
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
        PlatformTransmissionRecord? latest = Latest(records);
        return latest is null ? null : ToTransmission(latest);
    }

    /// <summary>La transmission soumise en dernier parmi celles d'une pièce.</summary>
    internal static PlatformTransmissionRecord? Latest(IEnumerable<PlatformTransmissionRecord> records) =>
        records.OrderBy(r => r.SubmittedAtMs).ThenBy(r => r.Id).LastOrDefault();

    internal static PlatformTransmission ToTransmission(PlatformTransmissionRecord record) => new(
        record.Id,
        record.InvoiceId,
        record.InvoiceNumber,
        (PlatformChannel)record.Channel,
        (EInvoiceFormat?)record.Format,
        record.Platform,
        record.Reference,
        record.TotalInclTax,
        DateTimeOffset.FromUnixTimeMilliseconds(record.SubmittedAtMs),
        record.Events.OrderBy(e => e.Sequence).Select(e => new PlatformStatusEvent(
            (PlatformStatus)e.Status, DateTimeOffset.FromUnixTimeMilliseconds(e.AtMs), e.Reason, e.Amount)));

    public async Task StageAsync(PlatformTransmission transmission, CancellationToken cancellationToken)
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
    }
}

/// <summary>
/// L'empreinte de chaque statut de chaque transmission du dossier, recalculée depuis la base pour le contrôle
/// d'intégrité (comparée au journal inaltérable).
/// </summary>
internal sealed class EfPlatformHistoryReader(TenantDbSession session) : IPlatformHistoryReader
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ComputeFingerprintsAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return records.ToDictionary(
            r => r.Id,
            r => (IReadOnlyList<string>)[.. r.Events.OrderBy(e => e.Sequence).Select(e => PlatformEventFingerprint.Compute(
                r.Id,
                e.Sequence,
                r.Platform,
                r.Reference,
                r.Channel,
                r.Format,
                e.Status,
                DateTimeOffset.FromUnixTimeMilliseconds(e.AtMs),
                e.Reason,
                e.Amount))]);
    }
}
