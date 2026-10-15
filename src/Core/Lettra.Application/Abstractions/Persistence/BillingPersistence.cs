using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Critères communs des listes de devis et de factures (dates d'émission bornes comprises).</summary>
public sealed record BillingListFilter(
    string? Search,
    CustomerId? Customer,
    DateOnly? From,
    DateOnly? To,
    int Page,
    int PageSize);

public interface IQuoteRepository
{
    Task AddAsync(Quote quote, CancellationToken cancellationToken);

    Task<Quote?> FindAsync(QuoteId id, CancellationToken cancellationToken);

    void Remove(Quote quote);
}

public interface IQuoteReadStore
{
    Task<PagedResponse<QuoteSummary>> ListAsync(BillingListFilter filter, QuoteStatus? status, DateOnly today, CancellationToken cancellationToken);
}

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    Task<Invoice?> FindAsync(InvoiceId id, CancellationToken cancellationToken);

    /// <summary>Factures d'acompte d'un devis (brouillons compris).</summary>
    Task<IReadOnlyList<Invoice>> ListDownPaymentsAsync(QuoteId quote, CancellationToken cancellationToken);

    void Remove(Invoice invoice);
}

public interface IInvoiceReadStore
{
    Task<PagedResponse<InvoiceSummary>> ListAsync(BillingListFilter filter, InvoiceKind? kind, InvoiceStatus? status, CancellationToken cancellationToken);
}

public interface ICompanyProfileRepository
{
    Task<CompanyProfile?> FindAsync(CancellationToken cancellationToken);

    Task AddAsync(CompanyProfile profile, CancellationToken cancellationToken);
}

/// <summary>Écriture du journal des ventes : une écriture équilibrée par pièce validée.</summary>
public interface ISalesJournal
{
    Task AddAsync(SalesJournalEntry entry, CancellationToken cancellationToken);
}

/// <summary>Écriture comptable d'une pièce validée, telle que le FEC l'exportera (phase 5).</summary>
public sealed record SalesJournalEntry(
    InvoiceId InvoiceId,
    string PieceNumber,
    DateOnly Date,
    CustomerId CustomerId,
    string CustomerName,
    IReadOnlyList<AccountingEntryLine> Lines);

public interface IPriceListRepository
{
    Task AddAsync(PriceList list, CancellationToken cancellationToken);

    Task<PriceList?> FindAsync(PriceListId id, CancellationToken cancellationToken);

    /// <summary>Grilles (autres que <paramref name="except"/>) où figure l'un de ces clients.</summary>
    Task<IReadOnlyList<PriceList>> ListContainingAsync(IReadOnlyCollection<CustomerId> customers, PriceListId except, CancellationToken cancellationToken);

    Task<PriceList?> FindForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    void Remove(PriceList list);
}

public interface IPriceListReadStore
{
    Task<PagedResponse<PriceListSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<PriceListResponse?> GetAsync(PriceListId id, CancellationToken cancellationToken);
}

/// <summary>Existence des références d'une saisie (clients, articles), en une requête par type.</summary>
public interface IBillingReferenceChecker
{
    Task<IReadOnlySet<CustomerId>> ExistingCustomersAsync(IReadOnlyCollection<CustomerId> ids, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<CatalogItemId, CatalogItem>> FindCatalogItemsAsync(IReadOnlyCollection<CatalogItemId> ids, CancellationToken cancellationToken);
}
