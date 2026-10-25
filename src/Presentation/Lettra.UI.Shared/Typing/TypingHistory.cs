using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared.Services;

namespace Lettra.UI.Shared.Typing;

/// <summary>Une pièce passée, réduite à ce qui sert aux suggestions.</summary>
public sealed record HistoryDocument(Guid CustomerId, DateOnly? Date, string? PaymentTerms, IReadOnlyList<HistoryLine> Lines);

public sealed record HistoryLine(Guid? CatalogItemId, string? Reference, string Designation);

/// <summary>Un article que le client prend souvent : nombre de pièces où il figure, dernière désignation employée.</summary>
public sealed record HabitItem(Guid CatalogItemId, string? Reference, string Designation, int Uses, DateOnly? LastUsed);

/// <summary>
/// Habitudes tirées des pièces du dossier (vague 5, lane F), calculées dans l'application, sans IA : articles habituels
/// d'un client, ses dernières conditions de règlement, désignations déjà écrites pour un article. Calcul pur sur une
/// liste de pièces, pour être testé et mesuré sans écran.
/// </summary>
public sealed class DossierHistory
{
    private readonly IReadOnlyList<HistoryDocument> _documents;

    public DossierHistory(IEnumerable<HistoryDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = [.. documents.OrderByDescending(d => d.Date ?? DateOnly.MinValue)];
    }

    public static DossierHistory Empty { get; } = new([]);

    public int Count => _documents.Count;

    /// <summary>Articles du catalogue les plus souvent facturés ou proposés au client, du plus fréquent au moins fréquent.</summary>
    public IReadOnlyList<HabitItem> ItemsFor(Guid customerId, int max = 5) =>
    [
        .. _documents.Where(d => d.CustomerId == customerId)
            .SelectMany(d => d.Lines.Where(l => l.CatalogItemId is not null).DistinctBy(l => l.CatalogItemId).Select(l => (Document: d, Line: l)))
            .GroupBy(x => x.Line.CatalogItemId!.Value)
            .Select(g => new HabitItem(g.Key, g.First().Line.Reference, g.First().Line.Designation, g.Count(), g.First().Document.Date))
            .OrderByDescending(h => h.Uses)
            .ThenByDescending(h => h.LastUsed ?? DateOnly.MinValue)
            .Take(max),
    ];

    /// <summary>Conditions de règlement de la dernière facture du client qui en portait.</summary>
    public string? LastPaymentTermsFor(Guid customerId) =>
        _documents.FirstOrDefault(d => d.CustomerId == customerId && !string.IsNullOrWhiteSpace(d.PaymentTerms))?.PaymentTerms;

    /// <summary>
    /// Désignations déjà employées : pour un article, celles écrites sur ses lignes ; sans article, toutes, de la plus
    /// fréquente à la moins fréquente.
    /// </summary>
    public IReadOnlyList<string> DesignationsFor(Guid? catalogItemId, int max = 8) =>
    [
        .. _documents.SelectMany(d => d.Lines)
            .Where(l => catalogItemId is null || l.CatalogItemId == catalogItemId)
            .Select(l => l.Designation.Trim())
            .Where(d => d.Length > 0)
            .GroupBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .Take(max),
    ];
}

/// <summary>
/// Lit les pièces récentes une fois par session (et celles d'un client à sa sélection) au travers des gateways
/// existantes. Les suggestions sont ensuite servies de la mémoire, sans attendre le réseau.
/// </summary>
public sealed class TypingHistory(Optional<IInvoicesGateway> invoiceGateway, Optional<IQuotesGateway> quoteGateway)
{
    /// <summary>Pièces lues au plus pour les désignations, et pour un client.</summary>
    public const int RecentDocuments = 40;

    public const int CustomerDocuments = 20;

    private const int Parallelism = 6;

    private readonly Dictionary<Guid, Task<DossierHistory>> _byCustomer = [];
    private Task<DossierHistory>? _recent;

    /// <summary>Historique récent du dossier (désignations).</summary>
    public Task<DossierHistory> RecentAsync() => _recent is { IsFaulted: false } recent ? recent : _recent = LoadAsync(null, RecentDocuments);

    /// <summary>Historique d'un client (articles habituels, conditions de règlement).</summary>
    public Task<DossierHistory> ForCustomerAsync(Guid customerId)
    {
        if (!_byCustomer.TryGetValue(customerId, out Task<DossierHistory>? task) || task.IsFaulted)
        {
            _byCustomer[customerId] = task = LoadAsync(customerId, CustomerDocuments);
        }

        return task;
    }

    /// <summary>Une pièce a été enregistrée : la prochaine suggestion relit les pièces.</summary>
    public void Invalidate()
    {
        _recent = null;
        _byCustomer.Clear();
    }

    private async Task<DossierHistory> LoadAsync(Guid? customerId, int max)
    {
        List<Func<Task<HistoryDocument?>>> reads = [];
        if (invoiceGateway.Value is { } invoices)
        {
            OperationResult<PagedResponse<InvoiceSummary>> list =
                await invoices.ListAsync(new BillingListQuery(CustomerId: customerId, PageSize: max / 2), null, null, CancellationToken.None).ConfigureAwait(false);
            reads.AddRange((list.Value?.Items ?? []).Where(i => i.Kind != InvoiceKindDto.CreditNote).Select(i => (Func<Task<HistoryDocument?>>)(async () =>
            {
                OperationResult<InvoiceResponse> invoice = await invoices.GetAsync(i.Id, CancellationToken.None).ConfigureAwait(false);
                return invoice.Value is { } v ? new HistoryDocument(v.CustomerId, v.IssueDate ?? DateOnly.FromDateTime(v.CreatedAt.LocalDateTime), v.PaymentTerms, Lines(v.Lines)) : null;
            })));
        }

        if (quoteGateway.Value is { } quotes)
        {
            OperationResult<PagedResponse<QuoteSummary>> list =
                await quotes.ListAsync(new BillingListQuery(CustomerId: customerId, PageSize: max / 2), null, CancellationToken.None).ConfigureAwait(false);
            reads.AddRange((list.Value?.Items ?? []).Select(q => (Func<Task<HistoryDocument?>>)(async () =>
            {
                OperationResult<QuoteResponse> quote = await quotes.GetAsync(q.Id, CancellationToken.None).ConfigureAwait(false);
                return quote.Value is { } v ? new HistoryDocument(v.CustomerId, v.IssueDate ?? DateOnly.FromDateTime(v.CreatedAt.LocalDateTime), null, Lines(v.Lines)) : null;
            })));
        }

        List<HistoryDocument> documents = [];
        foreach (Func<Task<HistoryDocument?>>[] batch in reads.Chunk(Parallelism))
        {
            HistoryDocument?[] read = await Task.WhenAll(batch.Select(r => r())).ConfigureAwait(false);
            documents.AddRange(read.OfType<HistoryDocument>());
        }

        return new DossierHistory(documents);
    }

    private static HistoryLine[] Lines(IReadOnlyList<DocumentLineResponse> lines) =>
        [.. lines.Select(l => new HistoryLine(l.CatalogItemId, l.Reference, l.Designation))];
}
