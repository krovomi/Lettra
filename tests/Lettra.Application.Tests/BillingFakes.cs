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
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>
/// Faux dépôts des devis, factures, fiche entreprise, grilles et journaux. La numérotation compte par série et ne
/// consomme pas le numéro si le travail échoue ; le journal d'intégrité garde les inscriptions dans l'ordre.
/// </summary>
internal sealed class InMemoryBilling :
    IQuoteRepository, IQuoteReadStore, IInvoiceRepository, IInvoiceReadStore, ICompanyProfileRepository, ISalesJournal,
    IPriceListRepository, IPriceListReadStore, IDocumentNumbering, IIntegrityJournal
{
    public Dictionary<QuoteId, Quote> Quotes { get; } = [];

    public Dictionary<InvoiceId, Invoice> Invoices { get; } = [];

    public CompanyProfile? Profile { get; set; }

    public List<SalesJournalEntry> SalesEntries { get; } = [];

    public Dictionary<PriceListId, PriceList> PriceLists { get; } = [];

    public Dictionary<string, long> NextNumbers { get; } = new(StringComparer.Ordinal);

    public List<DocumentLifecycleEvent> Journal { get; } = [];

    public Task AddAsync(Quote quote, CancellationToken cancellationToken)
    {
        Quotes[quote.Id] = quote;
        return Task.CompletedTask;
    }

    public Task<Quote?> FindAsync(QuoteId id, CancellationToken cancellationToken) => Task.FromResult(Quotes.GetValueOrDefault(id));

    public Task RemoveAsync(Quote quote, CancellationToken cancellationToken)
    {
        Quotes.Remove(quote.Id);
        return Task.CompletedTask;
    }

    public Task<PagedResponse<QuoteSummary>> ListAsync(BillingListFilter filter, QuoteStatus? status, DateOnly today, CancellationToken cancellationToken)
    {
        List<QuoteSummary> items = [.. Quotes.Values
            .Where(q => status is null || q.Status == status)
            .Select(q => new QuoteSummary(q.Id.Value, q.Number, (QuoteStatusDto)(int)q.Status, q.CustomerId.Value, q.Customer.Name, q.IssueDate,
                q.ValidUntil, q.IsExpired(today), q.TotalExclTax, q.TotalInclTax))];
        return Task.FromResult(new PagedResponse<QuoteSummary>(items, filter.Page, filter.PageSize, items.Count));
    }

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        Invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken) => Task.FromResult(Invoices.GetValueOrDefault(id));

    public Task<IReadOnlyList<Invoice>> ListDownPaymentsAsync(QuoteId quote, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Invoice>>([.. Invoices.Values.Where(i => i.QuoteId == quote && i.Kind == InvoiceKind.DownPayment)]);

    public Task RemoveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        Invoices.Remove(invoice.Id);
        return Task.CompletedTask;
    }

    public Task<PagedResponse<InvoiceSummary>> ListAsync(BillingListFilter filter, InvoiceKind? kind, InvoiceStatus? status, CancellationToken cancellationToken)
    {
        List<InvoiceSummary> items = [.. Invoices.Values
            .Where(i => (kind is null || i.Kind == kind) && (status is null || i.Status == status))
            .Select(i => new InvoiceSummary(i.Id.Value, (InvoiceKindDto)(int)i.Kind, (InvoiceStatusDto)(int)i.Status, i.Number, i.CustomerId.Value,
                i.Customer.Name, i.IssueDate, i.DueDate, i.TotalExclTax, i.TotalInclTax))];
        return Task.FromResult(new PagedResponse<InvoiceSummary>(items, filter.Page, filter.PageSize, items.Count));
    }

    public Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Profile);

    public Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken)
    {
        Profile = profile;
        return Task.CompletedTask;
    }

    public Task AddAsync(SalesJournalEntry entry, CancellationToken cancellationToken)
    {
        SalesEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task AddAsync(PriceList list, CancellationToken cancellationToken)
    {
        PriceLists[list.Id] = list;
        return Task.CompletedTask;
    }

    public Task<PriceList?> FindAsync(PriceListId id, CancellationToken cancellationToken) => Task.FromResult(PriceLists.GetValueOrDefault(id));

    public Task<IReadOnlyList<PriceList>> ListContainingAsync(IReadOnlyCollection<CustomerId> customers, PriceListId except, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PriceList>>([.. PriceLists.Values.Where(l => l.Id != except && l.Customers.Any(c => customers.Contains(c.CustomerId)))]);

    public Task<PriceList?> FindForCustomerAsync(CustomerId customer, CancellationToken cancellationToken) =>
        Task.FromResult(PriceLists.Values.FirstOrDefault(l => l.Customers.Any(c => c.CustomerId == customer)));

    public Task RemoveAsync(PriceList list, CancellationToken cancellationToken)
    {
        PriceLists.Remove(list.Id);
        return Task.CompletedTask;
    }

    public Task<PagedResponse<PriceListSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        List<PriceListSummary> items = [.. PriceLists.Values.Select(l => l.ToSummary())];
        return Task.FromResult(new PagedResponse<PriceListSummary>(items, page, pageSize, items.Count));
    }

    public Task<PriceListResponse?> GetAsync(PriceListId id, CancellationToken cancellationToken) =>
        Task.FromResult(PriceLists.GetValueOrDefault(id) is { } l
            ? new PriceListResponse(l.Id.Value, l.Version, l.Name, [.. l.Customers.Select(c => c.CustomerId.Value)],
                [.. l.Entries.Select(e => new PriceListEntryResponse(e.CatalogItemId.Value, string.Empty, string.Empty, e.UnitPriceExclTax, e.DiscountPercent))],
                l.UpdatedAt)
            : null);

    public async Task<Result<T>> ExecuteWithNextNumberAsync<T>(string seriesCode, Func<string, CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        long next = NextNumbers.GetValueOrDefault(seriesCode, 1);
        int journaled = Journal.Count;
        int entries = SalesEntries.Count;
        Result<T> result = await work(seriesCode + next.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        if (result.IsSuccess)
        {
            NextNumbers[seriesCode] = next + 1;
        }
        else
        {
            Journal.RemoveRange(journaled, Journal.Count - journaled);
            SalesEntries.RemoveRange(entries, SalesEntries.Count - entries);
        }

        return result;
    }

    public Task<Result<long>> AppendAsync(DocumentLifecycleEvent @event, CancellationToken ct)
    {
        Journal.Add(@event);
        return Task.FromResult(Result<long>.Success(Journal.Count));
    }
}

internal sealed class FakeBillingReferenceChecker(ICustomerRepository customers, ICatalogItemRepository items) : IBillingReferenceChecker
{
    public async Task<IReadOnlySet<CustomerId>> ExistingCustomersAsync(IReadOnlyCollection<CustomerId> ids, CancellationToken cancellationToken)
    {
        HashSet<CustomerId> found = [];
        foreach (CustomerId id in ids)
        {
            if (await customers.FindAsync(id, cancellationToken) is not null)
            {
                found.Add(id);
            }
        }

        return found;
    }

    public async Task<IReadOnlyDictionary<CatalogItemId, CatalogItem>> FindCatalogItemsAsync(IReadOnlyCollection<CatalogItemId> ids, CancellationToken cancellationToken)
    {
        Dictionary<CatalogItemId, CatalogItem> found = [];
        foreach (CatalogItemId id in ids)
        {
            if (await items.FindAsync(id, cancellationToken) is { } item)
            {
                found[id] = item;
            }
        }

        return found;
    }
}

internal static class BillingFakesRegistration
{
    public static IServiceCollection AddBillingFakes(this IServiceCollection services, InMemoryBilling billing)
    {
        services.AddSingleton<IQuoteRepository>(billing);
        services.AddSingleton<IQuoteReadStore>(billing);
        services.AddSingleton<IInvoiceRepository>(billing);
        services.AddSingleton<IInvoiceReadStore>(billing);
        services.AddSingleton<ICompanyProfileRepository>(billing);
        services.AddSingleton<ISalesJournal>(billing);
        services.AddSingleton<IPriceListRepository>(billing);
        services.AddSingleton<IPriceListReadStore>(billing);
        services.AddSingleton<IDocumentNumbering>(billing);
        services.AddSingleton<IIntegrityJournal>(billing);
        services.AddSingleton<IBillingReferenceChecker, FakeBillingReferenceChecker>();
        return services;
    }
}
