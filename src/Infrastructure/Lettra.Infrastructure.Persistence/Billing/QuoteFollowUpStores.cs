using System.Globalization;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Lettra.Infrastructure.Persistence.Billing;

// Devis à relancer et carnet de commandes, dans la base du dossier. Comme pour les relances de factures, les tris sur
// les dates avec fuseau et la recherche sans accents se font en mémoire, sur des volumes de petite entreprise.

/// <summary>Les devis numérotés réduits à ce que lisent les devis à relancer et le carnet de commandes.</summary>
internal sealed class EfQuotePipelineReader(TenantDbSession session) : IQuotePipelineReader
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<QuoteFact>> ListAsync(DateOnly from, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Set<Quote>().AsNoTracking()
            .Where(q => q.Status != QuoteStatus.Draft && q.Number != null && q.IssueDate != null
                && (q.IssueDate >= from || q.Status == QuoteStatus.Validated))
            .Select(q => new
            {
                q.Id,
                q.Number,
                q.CustomerId,
                q.Customer.Name,
                q.Status,
                q.IssueDate,
                q.ValidUntil,
                q.RespondedOn,
                q.UpdatedAt,
                q.TotalExclTax,
                q.TotalInclTax,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. rows.Select(r => new QuoteFact(
                r.Id,
                r.Number!,
                r.CustomerId,
                r.Name,
                r.Status,
                r.IssueDate!.Value,
                r.ValidUntil,
                AnsweredOn(r.Status, r.RespondedOn, r.UpdatedAt),
                r.TotalExclTax,
                r.TotalInclTax)),
        ];
    }

    public async Task<IReadOnlyDictionary<CustomerId, QuoteCustomerContact>> ContactsAsync(IReadOnlyCollection<CustomerId> customers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customers);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<CustomerId, QuoteCustomerContact> found = [];
        foreach (CustomerId[] batch in customers.Distinct().Chunk(BatchSize))
        {
            List<Customer> rows = await db.Set<Customer>().AsNoTracking().Where(c => batch.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (Customer c in rows)
            {
                found[c.Id] = new QuoteCustomerContact(c.Email?.Value, c.PreferredLanguage);
            }
        }

        return found;
    }

    /// <summary>
    /// Le jour de la réponse ; pour un devis accepté ou refusé avant que ce jour soit gardé, celui de sa dernière
    /// modification (la réponse, sauf acompte facturé depuis). Un devis déjà facturé n'en a pas : sa dernière
    /// modification est la facture.
    /// </summary>
    private static DateOnly? AnsweredOn(QuoteStatus status, DateOnly? respondedOn, DateTimeOffset updatedAt) =>
        respondedOn ?? (status is QuoteStatus.Accepted or QuoteStatus.Refused ? LegalCalendar.DateOf(updatedAt) : null);
}

internal sealed class EfQuoteFollowUpRepository(TenantDbSession session) : IQuoteFollowUpRepository
{
    private const int BatchSize = 500;

    public async Task AddAsync(QuoteFollowUp followUp, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<QuoteFollowUp>().AddAsync(followUp, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<QuoteId, QuoteFollowUpStats>> StatsAsync(IReadOnlyCollection<QuoteId> quotes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<QuoteId, QuoteFollowUpStats> stats = [];
        foreach (QuoteId[] batch in quotes.Distinct().Chunk(BatchSize))
        {
            List<QuoteFollowUp> sent = await db.Set<QuoteFollowUp>().AsNoTracking()
                .Where(f => batch.Contains(f.QuoteId) && f.Outcome == QuoteFollowUpOutcome.Sent)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (IGrouping<QuoteId, QuoteFollowUp> group in sent.GroupBy(f => f.QuoteId))
            {
                stats[group.Key] = new QuoteFollowUpStats(group.Count(), group.MaxBy(f => f.OccurredAt)!);
            }
        }

        return stats;
    }

    public async Task<(IReadOnlyList<QuoteFollowUpHistoryItem> Items, int Total)> ListAsync(QuoteFollowUpHistoryFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<QuoteFollowUp> query = db.Set<QuoteFollowUp>().AsNoTracking();
        if (filter.Quote is QuoteId quote)
        {
            query = query.Where(f => f.QuoteId == quote);
        }

        if (filter.Customer is CustomerId customer)
        {
            query = query.Where(f => f.CustomerId == customer);
        }

        if (filter.Channel is QuoteFollowUpChannel channel)
        {
            query = query.Where(f => f.Channel == channel);
        }

        if (filter.Outcome is QuoteFollowUpOutcome outcome)
        {
            query = query.Where(f => f.Outcome == outcome);
        }

        if (filter.From is DateOnly from)
        {
            query = query.Where(f => f.OccurredOn >= from);
        }

        if (filter.To is DateOnly to)
        {
            query = query.Where(f => f.OccurredOn <= to);
        }

        List<QuoteFollowUp> rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        CustomerId[] ids = [.. rows.Select(f => f.CustomerId).Distinct()];
        Dictionary<CustomerId, string> names = ids.Length == 0
            ? []
            : await db.Set<Customer>().AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken).ConfigureAwait(false);
        IEnumerable<QuoteFollowUpHistoryItem> items = rows.Select(f => new QuoteFollowUpHistoryItem(f, names.GetValueOrDefault(f.CustomerId, "")));
        if (filter.Search is string search)
        {
            CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            items = items.Where(i => compare.IndexOf(i.FollowUp.QuoteNumber, search, options) >= 0
                || compare.IndexOf(i.CustomerName, search, options) >= 0
                || compare.IndexOf(i.FollowUp.Subject, search, options) >= 0
                || (i.FollowUp.Recipient is string recipient && compare.IndexOf(recipient, search, options) >= 0));
        }

        List<QuoteFollowUpHistoryItem> filtered = [.. Sort(items, filter.Sort, filter.Descending)];
        return ([.. filtered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)], filtered.Count);
    }

    private static IEnumerable<QuoteFollowUpHistoryItem> Sort(IEnumerable<QuoteFollowUpHistoryItem> items, QuoteFollowUpSortDto sort, bool descending)
    {
        IOrderedEnumerable<QuoteFollowUpHistoryItem> ordered = sort switch
        {
            QuoteFollowUpSortDto.Quote => Order(items, i => i.FollowUp.QuoteNumber, descending, StringComparer.Ordinal),
            QuoteFollowUpSortDto.Customer => Order(items, i => i.CustomerName, descending, StringComparer.CurrentCultureIgnoreCase),
            QuoteFollowUpSortDto.Amount => Order(items, i => i.FollowUp.AmountExclTax, descending, Comparer<decimal>.Default),
            _ => Order(items, i => i.FollowUp.OccurredAt, descending, Comparer<DateTimeOffset>.Default),
        };
        return ordered.ThenByDescending(i => i.FollowUp.OccurredAt);
    }

    private static IOrderedEnumerable<QuoteFollowUpHistoryItem> Order<TKey>(
        IEnumerable<QuoteFollowUpHistoryItem> items, Func<QuoteFollowUpHistoryItem, TKey> key, bool descending, IComparer<TKey> comparer) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
}

internal sealed class EfQuoteFollowUpSettingsRepository(TenantDbSession session) : IQuoteFollowUpSettingsRepository
{
    public async Task<QuoteFollowUpSettings?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<QuoteFollowUpSettings>()
            .FirstOrDefaultAsync(s => s.Id == QuoteFollowUpSettings.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(QuoteFollowUpSettings settings, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<QuoteFollowUpSettings>().AddAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
