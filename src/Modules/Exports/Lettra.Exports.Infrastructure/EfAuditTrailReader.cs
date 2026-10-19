using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.AuditKit;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// La piste d'audit fiable d'une période dans la base du dossier : pièces validées (tables <c>Invoices</c> et lignes),
/// toutes leurs transmissions à la plateforme (<c>PlatformTransmissions</c>, <c>PlatformStatusEvents</c>) et les
/// encaissements (<c>Payments</c>). Tri en mémoire, comme le reste du module (ordre des GUID propre à chaque base).
/// </summary>
internal sealed class EfAuditTrailReader(TenantDbSession session) : IAuditTrailReader
{
    // Sous la limite de 2 100 paramètres de SQL Server.
    private const int BatchSize = 1000;

    public async Task<AuditTrailData> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Invoice> invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Number != null && i.IssueDate != null && i.IssueDate >= from && i.IssueDate <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PlatformTransmissionRecord> records = [];
        foreach (Guid[] batch in invoices.Select(i => i.Id.Value).Chunk(BatchSize))
        {
            records.AddRange(await db.Set<PlatformTransmissionRecord>()
                .AsNoTracking()
                .Include(r => r.Events)
                .Where(r => batch.Contains(r.InvoiceId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        List<Payment> payments = await db.Set<Payment>()
            .AsNoTracking()
            .Where(p => p.Date >= from && p.Date <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuditTrailData(
            [.. invoices.OrderBy(i => i.IssueDate).ThenBy(i => i.Number, StringComparer.Ordinal)],
            [.. records.OrderBy(r => r.SubmittedAtMs).ThenBy(r => r.Id).Select(EfPlatformTransmissionStore.ToTransmission)],
            [.. payments.OrderBy(p => p.Number, StringComparer.Ordinal)]);
    }
}
