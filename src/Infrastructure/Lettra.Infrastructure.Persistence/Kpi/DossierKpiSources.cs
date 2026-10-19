using Lettra.Application.Abstractions.Kpi;
using Lettra.Application.Features.Kpi;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Sync;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Kpi;

/// <summary>
/// Les pièces validées du dossier courant, réduites à ce que le calcul des indicateurs lit. Les totaux sont lus pour le
/// calcul et ne quittent pas le processus (<see cref="BillingKpiCalculator"/>).
/// </summary>
internal sealed class EfBillingKpiReader(TenantDbSession session) : IBillingKpiReader
{
    public async Task<BillingKpiFacts> ReadAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.IssueDate != null)
            .Select(i => new { i.Id, i.Kind, i.IssueDate, i.DueDate, i.CustomerId, i.TotalExclTax, i.TotalInclTax, i.CreditedInclTax, i.PaidInclTax })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var quotes = await db.Set<Quote>().AsNoTracking()
            .Where(q => q.Status != QuoteStatus.Draft && q.IssueDate != null)
            .Select(q => new { q.Status, q.IssueDate, q.ValidUntil })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Encaissements et refus déclarés par la plateforme agréée, comme la vue cabinet (factures en retard).
        List<PlatformTransmissionRecord> transmissions = await db.Set<PlatformTransmissionRecord>().AsNoTracking()
            .Include(t => t.Events)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var events = transmissions
            .SelectMany(t => t.Events.Select(e => (t.InvoiceId, e.Status, e.Amount)))
            .Where(e => e.Status == (int)PlatformStatusDto.Collected || e.Status == (int)PlatformStatusDto.Refused)
            .ToList();
        Dictionary<Guid, decimal> collected = events
            .Where(e => e.Status == (int)PlatformStatusDto.Collected)
            .GroupBy(e => e.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount ?? 0m));
        HashSet<Guid> refused = [.. events.Where(e => e.Status == (int)PlatformStatusDto.Refused).Select(e => e.InvoiceId)];

        return new BillingKpiFacts(
            [
                .. invoices.Select(i => new InvoiceKpiFact(
                    i.Kind, i.IssueDate!.Value, i.DueDate, i.CustomerId.Value, i.TotalExclTax, i.TotalInclTax, i.CreditedInclTax,
                    Math.Max(i.PaidInclTax, collected.GetValueOrDefault(i.Id.Value)), refused.Contains(i.Id.Value))),
            ],
            [.. quotes.Select(q => new QuoteKpiFact(q.Status, q.IssueDate!.Value, q.ValidUntil))]);
    }
}

/// <summary>
/// Indicateurs d'exploitation d'un dossier, pour les administrateurs : taille de sa base et postes synchronisés (combien,
/// lesquels se taisent). Aucun identifiant de poste ni de personne n'est publié.
/// </summary>
internal sealed class DossierOperationsKpiSource(TenantDbSession session, TimeProvider clock) : IDossierKpiSource
{
    /// <summary>Un poste actif muet depuis plus longtemps compte parmi les postes silencieux.</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<KpiSample>> CollectAsync(DateOnly today, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<KpiSample> samples = [];
        long? size = await DatabaseSizeAsync(db, cancellationToken).ConfigureAwait(false);
        if (size is long bytes)
        {
            samples.Add(new KpiSample(KpiMetrics.DatabaseSize, bytes));
        }

        var devices = await db.Set<SyncDeviceRecord>().AsNoTracking()
            .Select(d => new { d.LastSeenAtMs, d.RevokedAtMs })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        long[] active = [.. devices.Where(d => d.RevokedAtMs is null).Select(d => d.LastSeenAtMs)];
        samples.Add(new KpiSample(KpiMetrics.Devices, active.Length, Label: new KpiLabel(KpiMetrics.Labels.State, "active")));
        samples.Add(new KpiSample(KpiMetrics.Devices, devices.Count - active.Length, Label: new KpiLabel(KpiMetrics.Labels.State, "revoked")));
        samples.Add(new KpiSample(KpiMetrics.SilentDevices, active.Count(seen => now - seen > SilentAfter.TotalMilliseconds)));
        if (active.Length > 0)
        {
            samples.Add(new KpiSample(KpiMetrics.OldestDeviceSyncAge, Math.Max(0, now - active.Min()) / 1000d));
        }

        return samples;
    }

    /// <summary>
    /// Taille occupée par le dossier : le fichier SQLite, les tables de son schéma PostgreSQL, les fichiers de
    /// la base SQL Server. Inconnue (pas d'indicateur) pour un autre fournisseur.
    /// </summary>
    internal static async Task<long?> DatabaseSizeAsync(LettraDbContext db, CancellationToken cancellationToken)
    {
        string? provider = db.Database.ProviderName;
        string schema = db.Model.GetDefaultSchema() ?? "public";
        FormattableString? sql = provider switch
        {
            not null when provider.Contains("Sqlite", StringComparison.Ordinal) =>
                $"SELECT page_count * page_size AS \"Value\" FROM pragma_page_count(), pragma_page_size()",
            not null when provider.Contains("Npgsql", StringComparison.Ordinal) =>
                $"SELECT COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint AS \"Value\" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind IN ('r', 'm') AND n.nspname = {schema}",
            not null when provider.Contains("SqlServer", StringComparison.Ordinal) =>
                $"SELECT CAST(SUM(CAST(size AS bigint)) * 8192 AS bigint) AS [Value] FROM sys.database_files",
            _ => null,
        };
        if (sql is null)
        {
            return null;
        }

        List<long> rows = await db.Database.SqlQuery<long>(sql).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count == 1 ? rows[0] : null;
    }
}
