using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.Billing;

internal sealed class QuoteRepository(TenantDbSession session) : IQuoteRepository
{
    public async Task AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Quote>().AddAsync(quote, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Quote?> FindAsync(QuoteId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Quote>().AsTracking().FirstOrDefaultAsync(q => q.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Quote quote, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<Quote>().Remove(quote);
    }
}

internal sealed class QuoteReadStore(TenantDbSession session) : IQuoteReadStore
{
    public async Task<PagedResponse<QuoteSummary>> ListAsync(BillingListFilter filter, QuoteStatus? status, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Quote> query = BillingQueries.Filter(db.Set<Quote>().AsNoTracking(), filter);
        if (status is { } wanted)
        {
            query = query.Where(q => q.Status == wanted);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(q => q.IssueDate)
            .ThenByDescending(q => q.Number)
            .ThenBy(q => q.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(q => new { q.Id, q.Number, q.Status, q.CustomerId, q.Customer.Name, q.IssueDate, q.ValidUntil, q.TotalExclTax, q.TotalInclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<QuoteSummary> items = rows.Select(r => new QuoteSummary(
            r.Id.Value,
            r.Number,
            (QuoteStatusDto)(int)r.Status,
            r.CustomerId.Value,
            r.Name,
            r.IssueDate,
            r.ValidUntil,
            r.Status == QuoteStatus.Validated && r.ValidUntil < today,
            r.TotalExclTax,
            r.TotalInclTax)).ToList();
        return new PagedResponse<QuoteSummary>(items, filter.Page, filter.PageSize, total);
    }
}

internal sealed class InvoiceRepository(TenantDbSession session) : IInvoiceRepository
{
    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Invoice>().AddAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Invoice>().AsTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Invoice>> ListDownPaymentsAsync(QuoteId quote, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<Invoice> invoices = await db.Set<Invoice>().AsTracking()
            .Where(i => i.QuoteId == quote && i.Kind == InvoiceKind.DownPayment)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. invoices.OrderBy(i => i.Number, StringComparer.Ordinal)];
    }

    public async Task RemoveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<Invoice>().Remove(invoice);
    }
}

internal sealed class InvoiceReadStore(TenantDbSession session) : IInvoiceReadStore
{
    public async Task<PagedResponse<InvoiceSummary>> ListAsync(BillingListFilter filter, InvoiceKind? kind, InvoiceStatus? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Invoice> query = BillingQueries.Filter(db.Set<Invoice>().AsNoTracking(), filter);
        if (kind is { } wantedKind)
        {
            query = query.Where(i => i.Kind == wantedKind);
        }

        if (status is { } wantedStatus)
        {
            query = query.Where(i => i.Status == wantedStatus);
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.Number)
            .ThenBy(i => i.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(i => new { i.Id, i.Kind, i.Status, i.Number, i.CustomerId, i.Customer.Name, i.IssueDate, i.DueDate, i.TotalExclTax, i.TotalInclTax })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<InvoiceSummary> items = rows.Select(r => new InvoiceSummary(
            r.Id.Value,
            (InvoiceKindDto)(int)r.Kind,
            (InvoiceStatusDto)(int)r.Status,
            r.Number,
            r.CustomerId.Value,
            r.Name,
            r.IssueDate,
            r.DueDate,
            r.TotalExclTax,
            r.TotalInclTax)).ToList();
        return new PagedResponse<InvoiceSummary>(items, filter.Page, filter.PageSize, total);
    }
}

/// <summary>Filtres communs des listes de pièces, traduits à l'identique par les trois providers.</summary>
internal static class BillingQueries
{
    public static IQueryable<TDocument> Filter<TDocument>(IQueryable<TDocument> query, BillingListFilter filter)
        where TDocument : class
    {
        // Les deux agrégats partagent leurs colonnes ; on passe par EF.Property pour un seul filtre générique.
        if (filter.Customer is { } customer)
        {
            query = query.Where(d => EF.Property<CustomerId>(d, "CustomerId") == customer);
        }

        if (filter.From is { } from)
        {
            query = query.Where(d => EF.Property<DateOnly?>(d, "IssueDate") >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(d => EF.Property<DateOnly?>(d, "IssueDate") <= to);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            string term = search.ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // Arbre d'expression traduit en UPPER(...) SQL : jamais exécuté en .NET.
            query = query.Where(d =>
                (EF.Property<string?>(d, "Number") ?? string.Empty).ToUpper().Contains(term)
                || (EF.Property<string?>(d, "Subject") ?? string.Empty).ToUpper().Contains(term)
                || EF.Property<PartySnapshot>(d, "Customer").Name.ToUpper().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return query;
    }
}

internal sealed class CompanyProfileRepository(TenantDbSession session) : ICompanyProfileRepository
{
    public async Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<CompanyProfile>().AsTracking()
            .FirstOrDefaultAsync(p => p.Id == CompanyProfile.SingletonId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<CompanyProfile>().AddAsync(profile, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SalesJournal(TenantDbSession session) : ISalesJournal
{
    public async Task AddAsync(SalesJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        SalesJournalRecord record = new()
        {
            Id = Guid.CreateVersion7(),
            InvoiceId = entry.InvoiceId.Value,
            PieceNumber = entry.PieceNumber,
            Date = entry.Date,
            CustomerId = entry.CustomerId.Value,
            CustomerName = entry.CustomerName,
        };
        int number = 1;
        foreach (Calculations.AccountingEntryLine line in entry.Lines)
        {
            record.Lines.Add(new SalesJournalLineRecord
            {
                EntryId = record.Id,
                LineNumber = number++,
                Account = line.Account,
                Debit = line.Debit,
                Credit = line.Credit,
                VatRatePercent = line.VatRatePercent,
            });
        }

        await db.Set<SalesJournalRecord>().AddAsync(record, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class PriceListRepository(TenantDbSession session) : IPriceListRepository
{
    public async Task AddAsync(PriceList list, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<PriceList>().AddAsync(list, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PriceList?> FindAsync(PriceListId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PriceList>().AsTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PriceList>> ListContainingAsync(IReadOnlyCollection<CustomerId> customers, PriceListId except, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PriceList>().AsTracking()
            .Where(l => l.Id != except && l.Customers.Any(c => customers.Contains(c.CustomerId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PriceList?> FindForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<PriceList>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Customers.Any(c => c.CustomerId == customer), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(PriceList list, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<PriceList>().Remove(list);
    }
}

internal sealed class PriceListReadStore(TenantDbSession session) : IPriceListReadStore
{
    public async Task<PagedResponse<PriceListSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<PriceList> query = db.Set<PriceList>().AsNoTracking();
        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        List<PriceList> rows = await query
            .OrderBy(l => l.Name)
            .ThenBy(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PagedResponse<PriceListSummary>(rows.Select(r => r.ToSummary()).ToList(), page, pageSize, total);
    }

    public async Task<PriceListResponse?> GetAsync(PriceListId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        PriceList? list = await db.Set<PriceList>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return null;
        }

        List<CatalogItemId> itemIds = [.. list.Entries.Select(e => e.CatalogItemId)];
        var items = await db.Set<CatalogItem>().AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Reference, i.Designation })
            .ToDictionaryAsync(i => i.Id, cancellationToken)
            .ConfigureAwait(false);
        return new PriceListResponse(
            list.Id.Value,
            list.Version,
            list.Name,
            [.. list.Customers.Select(c => c.CustomerId.Value)],
            [.. list.Entries.Select(e => new PriceListEntryResponse(
                e.CatalogItemId.Value,
                items.TryGetValue(e.CatalogItemId, out var item) ? item.Reference : string.Empty,
                item?.Designation ?? string.Empty,
                e.UnitPriceExclTax,
                e.DiscountPercent))],
            list.UpdatedAt);
    }
}

internal sealed class BillingReferenceChecker(TenantDbSession session) : IBillingReferenceChecker
{
    public async Task<IReadOnlySet<CustomerId>> ExistingCustomersAsync(IReadOnlyCollection<CustomerId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new HashSet<CustomerId>();
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<CustomerId> found = await db.Customers.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return found.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<CatalogItemId, CatalogItem>> FindCatalogItemsAsync(IReadOnlyCollection<CatalogItemId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return new Dictionary<CatalogItemId, CatalogItem>();
        }

        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<CatalogItem>().AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Recalcule l'empreinte canonique des pièces relues en base, pour le contrôle d'intégrité de la chaîne anti-fraude :
/// une empreinte différente de celle du journal signale une pièce altérée hors de l'application.
/// </summary>
internal sealed class JournaledDocumentReader(TenantDbSession session) : IJournaledDocumentReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> ComputeContentHashesAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        List<InvoiceId> invoiceIds = [.. documentIds.Select(id => new InvoiceId(id))];
        List<QuoteId> quoteIds = [.. documentIds.Select(id => new QuoteId(id))];
        Dictionary<Guid, string> hashes = [];
        foreach (Invoice invoice in await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id) && i.Number != null).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            hashes[invoice.Id.Value] = CanonicalDocument.ComputeHash(invoice.ToResponse());
        }

        foreach (Quote quote in await db.Set<Quote>().AsNoTracking().Where(q => quoteIds.Contains(q.Id) && q.Number != null).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            // La date du jour ne sert qu'à « expiré », hors de la forme canonique.
            hashes[quote.Id.Value] = CanonicalDocument.ComputeHash(quote.ToResponse(DateOnly.MinValue));
        }

        return hashes;
    }
}

/// <summary>
/// Transaction explicite d'un cas d'usage des pièces (émission) : la pièce et l'entrée du journal inaltérable sont
/// enregistrées par le même <c>SaveChanges</c>, dans la même transaction, ou pas du tout.
/// </summary>
internal sealed class BillingTransaction(TenantDbSession session) : IBillingTransaction
{
    public async Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, work),
            static async (_, state, token) =>
            {
                state.db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await state.db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                Result result = await state.work(token).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    state.db.ChangeTracker.Clear();
                    return result;
                }

                await state.db.SaveChangesAsync(token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return result;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class BillingRegistration
{
    /// <summary>Dépôts des modules Devis, Facturation et Tarifs.</summary>
    public static IServiceCollection AddBillingStores(this IServiceCollection services)
    {
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IQuoteReadStore, QuoteReadStore>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceReadStore, InvoiceReadStore>();
        services.AddScoped<ICompanyProfileRepository, CompanyProfileRepository>();
        services.AddScoped<ISalesJournal, SalesJournal>();
        services.AddScoped<IPriceListRepository, PriceListRepository>();
        services.AddScoped<IPriceListReadStore, PriceListReadStore>();
        services.AddScoped<IBillingReferenceChecker, BillingReferenceChecker>();
        services.AddScoped<IJournaledDocumentReader, JournaledDocumentReader>();
        services.AddScoped<IBillingTransaction, BillingTransaction>();
        return services;
    }
}
