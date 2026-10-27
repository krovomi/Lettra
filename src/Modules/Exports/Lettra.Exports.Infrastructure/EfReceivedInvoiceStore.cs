using Lettra.Domain.Parties;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les factures fournisseurs reçues dans la base du dossier (tables <c>ReceivedInvoices</c> et
/// <c>ReceivedInvoiceStatusEvents</c>). Comme pour les transmissions, un enregistrement n'ajoute que les nouveaux statuts.
/// </summary>
internal sealed class EfReceivedInvoiceStore(TenantDbSession session) : IReceivedInvoiceStore
{
    public async Task<IReadOnlyList<ReceivedInvoice>> ListAsync(int max, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<ReceivedInvoiceRecord> records = await db.Set<ReceivedInvoiceRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .OrderByDescending(r => r.ReceivedAtMs)
            .Take(max)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Même instant de mise à disposition : l'ordre des identifiants (UUID v7), trié en mémoire comme ailleurs.
        return [.. records.OrderByDescending(r => r.ReceivedAtMs).ThenByDescending(r => r.Id).Select(ToInvoice)];
    }

    public async Task<ReceivedInvoice?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        ReceivedInvoiceRecord? record = await db.Set<ReceivedInvoiceRecord>()
            .AsNoTracking()
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : ToInvoice(record);
    }

    public async Task<IReadOnlySet<string>> KnownReferencesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<string> known = await db.Set<ReceivedInvoiceRecord>()
            .AsNoTracking()
            .Where(r => references.Contains(r.Reference))
            .Select(r => r.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return known.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> SuppliersBySirenAsync(IReadOnlyCollection<string> sirens, CancellationToken cancellationToken)
    {
        if (sirens.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        // Le SIREN est un objet valeur : les fournisseurs actifs sont peu nombreux, on compare en mémoire.
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Supplier> suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => !s.IsArchived && s.Siren != null).ToListAsync(cancellationToken).ConfigureAwait(false);
        return suppliers
            .Where(s => sirens.Contains(s.Siren!.Value))
            .GroupBy(s => s.Siren!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name, StringComparer.Ordinal).First().Id.Value, StringComparer.Ordinal);
    }

    public async Task StageAsync(ReceivedInvoice invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        bool exists = await db.Set<ReceivedInvoiceRecord>().AnyAsync(r => r.Id == invoice.Id, cancellationToken).ConfigureAwait(false);
        int known = 0;
        if (!exists)
        {
            ReceivedInvoiceHeader header = invoice.Header;
            db.Add(new ReceivedInvoiceRecord
            {
                Id = invoice.Id,
                Platform = invoice.Platform,
                Reference = invoice.Reference,
                Format = (int)invoice.Format,
                FileName = invoice.FileName,
                Content = invoice.Content.ToArray(),
                Kind = (int)header.Kind,
                Number = header.Number,
                IssueDate = header.IssueDate,
                DueDate = header.DueDate,
                SupplierName = header.SupplierName,
                SupplierSiren = header.SupplierSiren,
                SupplierVatNumber = header.SupplierVatNumber,
                SupplierId = invoice.SupplierId,
                BuyerSiren = header.BuyerSiren,
                Currency = header.Currency,
                TotalExclTax = header.TotalExclTax,
                TotalVat = header.TotalVat,
                TotalInclTax = header.TotalInclTax,
                BusinessProcess = header.BusinessProcess,
                ReceivedAtMs = invoice.ReceivedAt.ToUnixTimeMilliseconds(),
            });
        }
        else
        {
            known = await db.Set<ReceivedInvoiceStatusRecord>()
                .CountAsync(e => e.ReceivedInvoiceId == invoice.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        for (int sequence = known; sequence < invoice.History.Count; sequence++)
        {
            PlatformStatusEvent status = invoice.History[sequence];
            db.Add(new ReceivedInvoiceStatusRecord
            {
                ReceivedInvoiceId = invoice.Id,
                Sequence = sequence,
                Status = (int)status.Status,
                AtMs = status.At.ToUnixTimeMilliseconds(),
                Reason = status.Reason,
                Amount = status.Amount,
            });
        }
    }

    private static ReceivedInvoice ToInvoice(ReceivedInvoiceRecord record) => new(
        record.Id,
        record.Platform,
        record.Reference,
        (EInvoiceFormat)record.Format,
        record.FileName,
        record.Content,
        new ReceivedInvoiceHeader(
            (ReceivedInvoiceKind)record.Kind,
            record.Number,
            record.IssueDate,
            record.DueDate,
            record.SupplierName,
            record.SupplierSiren,
            record.SupplierVatNumber,
            record.BuyerSiren,
            record.Currency,
            record.TotalExclTax,
            record.TotalVat,
            record.TotalInclTax,
            record.BusinessProcess),
        record.SupplierId,
        record.Events.OrderBy(e => e.Sequence).Select(e => new PlatformStatusEvent(
            (PlatformStatus)e.Status, DateTimeOffset.FromUnixTimeMilliseconds(e.AtMs), e.Reason, e.Amount)));
}
