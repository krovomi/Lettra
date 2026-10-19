using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Exports.Application.Tax;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tax;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Les pièces validées de la base du dossier (tables <c>Invoices</c>, lignes et récapitulatifs de TVA), avec leur
/// transmission éventuelle à la plateforme agréée. L'option pour les débits se lit dans les mentions figées.
/// </summary>
internal sealed class EfTaxDocumentReader(TenantDbSession session) : ITaxDocumentReader
{
    // Sous la limite de 2 100 paramètres de SQL Server.
    private const int BatchSize = 1000;

    public async Task<IReadOnlyList<TaxDocument>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Invoice> invoices = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.IssueDate != null && i.IssueDate >= from && i.IssueDate <= to)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> transmitted = [];
        foreach (Guid[] batch in invoices.Select(i => i.Id.Value).Chunk(BatchSize))
        {
            List<Guid> found = await db.Set<PlatformTransmissionRecord>()
                .AsNoTracking()
                .Where(t => batch.Contains(t.InvoiceId))
                .Select(t => t.InvoiceId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            transmitted.UnionWith(found);
        }

        return invoices
            .OrderBy(i => i.IssueDate)
            .ThenBy(i => i.Number, StringComparer.Ordinal)
            .Select(i => new TaxDocument(
                i.Id.Value,
                i.Kind,
                i.Number ?? string.Empty,
                i.IssueDate!.Value,
                i.CustomerId.Value,
                i.Customer,
                i.VatRegime,
                i.VatOnDebits,
                [.. i.Lines.Select(l => new TaxLine(l.Nature, l.VatCategory, l.VatRatePercent, l.VatExemptionCode, l.TotalExclTax))],
                i.VatSummaries,
                transmitted.Contains(i.Id.Value)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListDraftsAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var drafts = await db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Draft)
            .Select(i => new { i.CreatedAt, i.Customer.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Filtré en mémoire : SQLite ne compare pas les DateTimeOffset. Un brouillon n'a pas de numéro : on montre son client.
        return [.. drafts.Where(d => d.CreatedAt < before).OrderBy(d => d.CreatedAt).Select(d => d.Name)];
    }
}

/// <summary>Réglages fiscaux et échéances déposées (tables <c>FiscalSettings</c> et <c>FiscalFilings</c>).</summary>
internal sealed class EfFiscalStore(TenantDbSession session) : IFiscalStore
{
    public async Task<FiscalSettings?> FindSettingsAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        FiscalSettingsRecord? record = await db.Set<FiscalSettingsRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == FiscalSettingsRecord.SingletonId, cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? null
            : new FiscalSettings(
                record.FiscalYearEndMonth, record.FiscalYearEndDay, (VatPeriodicityDto)record.VatPeriodicity, record.VatDeadlineDay,
                record.CorporateIncomeTax, record.Cfe, record.TrackedFrom, record.Version);
    }

    public async Task SaveSettingsAsync(FiscalSettings settings, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        FiscalSettingsRecord? record = await db.Set<FiscalSettingsRecord>()
            .FirstOrDefaultAsync(r => r.Id == FiscalSettingsRecord.SingletonId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new FiscalSettingsRecord { Id = FiscalSettingsRecord.SingletonId };
            db.Add(record);
        }

        record.FiscalYearEndMonth = settings.FiscalYearEndMonth;
        record.FiscalYearEndDay = settings.FiscalYearEndDay;
        record.VatPeriodicity = (int)settings.VatPeriodicity;
        record.VatDeadlineDay = settings.VatDeadlineDay;
        record.CorporateIncomeTax = settings.CorporateIncomeTax;
        record.Cfe = settings.Cfe;
        record.TrackedFrom = settings.TrackedFrom;
        record.Version = settings.Version;
        record.UpdatedAtMs = at.ToUnixTimeMilliseconds();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FiscalFiling>> ListFilingsAsync(DateOnly fromPeriodStart, DateOnly toPeriodStart, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<FiscalFilingRecord> records = await db.Set<FiscalFilingRecord>()
            .AsNoTracking()
            .Where(r => r.PeriodStart >= fromPeriodStart && r.PeriodStart <= toPeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. records.Select(r => new FiscalFiling((FiscalObligationDto)r.Obligation, r.PeriodStart, DateTimeOffset.FromUnixTimeMilliseconds(r.FiledAtMs), r.FiledBy))];
    }

    public async Task SetFilingAsync(FiscalObligationDto obligation, DateOnly periodStart, FiscalFiling? filing, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        FiscalFilingRecord? record = await db.Set<FiscalFilingRecord>()
            .FirstOrDefaultAsync(r => r.Obligation == (int)obligation && r.PeriodStart == periodStart, cancellationToken)
            .ConfigureAwait(false);
        if (filing is null)
        {
            if (record is not null)
            {
                db.Remove(record);
            }
        }
        else if (record is null)
        {
            db.Add(new FiscalFilingRecord
            {
                Obligation = (int)obligation,
                PeriodStart = periodStart,
                FiledAtMs = filing.FiledAt.ToUnixTimeMilliseconds(),
                FiledBy = filing.FiledBy,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
