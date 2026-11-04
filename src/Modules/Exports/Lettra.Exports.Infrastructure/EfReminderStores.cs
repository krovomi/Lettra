using System.Globalization;
using Lettra.Contracts.Reminders;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Domain.Reminders;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Reminders;
using Lettra.Exports.Domain.Platform;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Exports.Infrastructure;

// Relances dans la base du dossier (vague 7). Comme pour les encaissements, les tris sur les dates avec fuseau et la
// recherche sans accents se font en mémoire, sur des volumes de petite entreprise.

internal sealed class EfReminderSettingsRepository(TenantDbSession session) : IReminderSettingsRepository
{
    public async Task<ReminderSettings?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<ReminderSettings>().FirstOrDefaultAsync(s => s.Id == ReminderSettings.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(ReminderSettings settings, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<ReminderSettings>().AddAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class EfPaymentReminderRepository(TenantDbSession session) : IPaymentReminderRepository
{
    private const int BatchSize = 500;

    public async Task AddAsync(PaymentReminder reminder, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PaymentReminder>().AddAsync(reminder, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<InvoiceId, ReminderTrack>> TracksAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InvoiceId, ReminderTrack> tracks = [];
        foreach (InvoiceId[] batch in invoices.Distinct().Chunk(BatchSize))
        {
            List<PaymentReminder> sent = await db.Set<PaymentReminder>().AsNoTracking()
                .Where(r => batch.Contains(r.InvoiceId) && r.Outcome == ReminderOutcome.Sent)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (IGrouping<InvoiceId, PaymentReminder> invoice in sent.GroupBy(r => r.InvoiceId))
            {
                tracks[invoice.Key] = ReminderTrack.Of(invoice);
            }
        }

        return tracks;
    }

    public async Task<(IReadOnlyList<ReminderHistoryItem> Items, int Total)> ListAsync(ReminderHistoryFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<PaymentReminder> query = db.Set<PaymentReminder>().AsNoTracking();
        if (filter.Invoice is InvoiceId invoice)
        {
            query = query.Where(r => r.InvoiceId == invoice);
        }

        if (filter.Customer is CustomerId customer)
        {
            query = query.Where(r => r.CustomerId == customer);
        }

        if (filter.Level is ReminderLevel level)
        {
            query = query.Where(r => r.Level == level);
        }

        if (filter.Outcome is ReminderOutcome outcome)
        {
            query = query.Where(r => r.Outcome == outcome);
        }

        if (filter.Channel is ReminderChannel channel)
        {
            query = query.Where(r => r.Channel == channel);
        }

        if (filter.From is DateOnly from)
        {
            query = query.Where(r => r.OccurredOn >= from);
        }

        if (filter.To is DateOnly to)
        {
            query = query.Where(r => r.OccurredOn <= to);
        }

        List<PaymentReminder> rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        CustomerId[] ids = [.. rows.Select(r => r.CustomerId).Distinct()];
        Dictionary<CustomerId, string> names = ids.Length == 0
            ? []
            : await db.Set<Customer>().AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken).ConfigureAwait(false);
        IEnumerable<ReminderHistoryItem> items = rows.Select(r => new ReminderHistoryItem(r, names.GetValueOrDefault(r.CustomerId, "")));
        if (filter.Search is string search)
        {
            CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            items = items.Where(i => compare.IndexOf(i.Reminder.InvoiceNumber, search, options) >= 0
                || compare.IndexOf(i.CustomerName, search, options) >= 0
                || compare.IndexOf(i.Reminder.Subject, search, options) >= 0
                || (i.Reminder.Recipient is string recipient && compare.IndexOf(recipient, search, options) >= 0));
        }

        List<ReminderHistoryItem> filtered = [.. Sort(items, filter.Sort, filter.Descending)];
        return ([.. filtered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)], filtered.Count);
    }

    private static IEnumerable<ReminderHistoryItem> Sort(IEnumerable<ReminderHistoryItem> items, ReminderSortDto sort, bool descending)
    {
        IOrderedEnumerable<ReminderHistoryItem> ordered = sort switch
        {
            ReminderSortDto.Invoice => Order(items, i => i.Reminder.InvoiceNumber, descending, StringComparer.Ordinal),
            ReminderSortDto.Customer => Order(items, i => i.CustomerName, descending, StringComparer.CurrentCultureIgnoreCase),
            ReminderSortDto.Level => Order(items, i => (int)i.Reminder.Level, descending, Comparer<int>.Default),
            _ => Order(items, i => i.Reminder.OccurredAt, descending, Comparer<DateTimeOffset>.Default),
        };
        return ordered.ThenByDescending(i => i.Reminder.OccurredAt);
    }

    private static IOrderedEnumerable<ReminderHistoryItem> Order<TKey>(
        IEnumerable<ReminderHistoryItem> items, Func<ReminderHistoryItem, TKey> key, bool descending, IComparer<TKey> comparer) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
}

internal sealed class EfReminderContextReader(TenantDbSession session) : IReminderContextReader
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyDictionary<CustomerId, ReminderCustomer>> CustomersAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<CustomerId, ReminderCustomer> found = [];
        foreach (CustomerId[] batch in customers.Distinct().Chunk(BatchSize))
        {
            List<Customer> rows = await db.Set<Customer>().AsNoTracking().Where(c => batch.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (Customer c in rows)
            {
                found[c.Id] = new ReminderCustomer(c.Id, c.Name, c.Email?.Value, c.PreferredLanguage, c.Kind == CustomerKind.Company);
            }
        }

        return found;
    }

    public async Task<IReadOnlyDictionary<InvoiceId, decimal>> PenaltyRatesAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<InvoiceId, decimal> rates = [];
        foreach (InvoiceId[] batch in invoices.Distinct().Chunk(BatchSize))
        {
            var rows = await db.Set<Invoice>().AsNoTracking()
                .Where(i => batch.Contains(i.Id) && i.LatePenaltyRate != null)
                .Select(i => new { i.Id, i.LatePenaltyRate })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                rates[row.Id] = row.LatePenaltyRate!.Value;
            }
        }

        return rates;
    }

    public async Task<ReminderSender?> SenderAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        CompanyProfile? profile = await db.Set<CompanyProfile>().AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? null : new ReminderSender(profile.Name, profile.Iban?.Value, profile.Bic);
    }

    public async Task<IReadOnlySet<InvoiceId>> DisputedAsync(IReadOnlyCollection<InvoiceId> invoices, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        HashSet<InvoiceId> disputed = [];
        foreach (Guid[] batch in invoices.Select(i => i.Value).Distinct().Chunk(BatchSize))
        {
            List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>().AsNoTracking()
                .Include(r => r.Events)
                .Where(r => batch.Contains(r.InvoiceId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            disputed.UnionWith(records.GroupBy(r => r.InvoiceId)
                .Where(g => EfPlatformTransmissionStore.ToTransmission(EfPlatformTransmissionStore.Latest(g)!).Status == PlatformStatus.Disputed)
                .Select(g => new InvoiceId(g.Key)));
        }

        return disputed;
    }
}

/// <summary>Échéanciers de la base du dossier, avec leurs échéances.</summary>
internal sealed class EfPaymentPlanRepository(TenantDbSession session) : IPaymentPlanRepository
{
    private const int BatchSize = 500;

    public async Task AddAsync(PaymentPlan plan, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PaymentPlan>().AddAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaymentPlan?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PaymentPlan>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<InvoiceId, PaymentPlan>> ActiveAsync(IReadOnlyCollection<InvoiceId>? invoices, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<PaymentPlan> active = db.Set<PaymentPlan>().AsNoTracking().Where(p => p.Status == PaymentPlanStatus.Active);
        List<PaymentPlan> found = [];
        if (invoices is null)
        {
            found.AddRange(await active.ToListAsync(cancellationToken).ConfigureAwait(false));
        }
        else
        {
            foreach (InvoiceId[] batch in invoices.Distinct().Chunk(BatchSize))
            {
                found.AddRange(await active.Where(p => batch.Contains(p.InvoiceId)).ToListAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        // Au plus un en cours par facture ; deux demandes simultanées gardent la première.
        return found.GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.RequestedAt).First());
    }

    public async Task<PaymentPlan?> LatestForInvoiceAsync(InvoiceId invoice, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<PaymentPlan> plans = await db.Set<PaymentPlan>().AsNoTracking().Where(p => p.InvoiceId == invoice).ToListAsync(cancellationToken).ConfigureAwait(false);
        return plans.OrderByDescending(p => p.Status == PaymentPlanStatus.Active).ThenByDescending(p => p.RequestedAt).FirstOrDefault();
    }

    public async Task<IReadOnlyList<PaymentPlan>> ListAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<PaymentPlan> plans = await db.Set<PaymentPlan>().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. plans.Where(p => p.Status == PaymentPlanStatus.Active || p.RequestedAt >= since)];
    }

    public async Task<int> CountUnacknowledgedAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PaymentPlan>().CountAsync(p => p.Status == PaymentPlanStatus.Active && p.AcknowledgedAt == null, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Oppositions au ciblage selon le comportement de paiement.</summary>
internal sealed class EfPaymentTargetingObjectionRepository(TenantDbSession session) : IPaymentTargetingObjectionRepository
{
    public async Task<IReadOnlyList<PaymentTargetingObjection>> ListAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PaymentTargetingObjection>().AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaymentTargetingObjection?> FindAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PaymentTargetingObjection>().FirstOrDefaultAsync(o => o.CustomerId == customer, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PaymentTargetingObjection>().AddAsync(objection, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(PaymentTargetingObjection objection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objection);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<PaymentTargetingObjection>().Remove(objection);
    }
}
