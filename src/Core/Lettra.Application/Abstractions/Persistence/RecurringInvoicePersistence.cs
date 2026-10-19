using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Persistence;

/// <summary>Critères de la liste des modèles récurrents (recherche dans le nom, l'objet et le client).</summary>
public sealed record RecurringInvoiceListFilter(
    string? Search,
    CustomerId? Customer,
    bool? Active,
    bool DueOnly,
    RecurringInvoiceSortDto Sort,
    bool Descending,
    DateOnly Today,
    int Page,
    int PageSize);

public interface IRecurringInvoiceRepository
{
    Task AddAsync(RecurringInvoice template, CancellationToken cancellationToken);

    Task<RecurringInvoice?> FindAsync(RecurringInvoiceId id, CancellationToken cancellationToken);

    /// <summary>Modèles actifs dont la prochaine facture est datée au plus tard <paramref name="today"/>.</summary>
    Task<IReadOnlyList<RecurringInvoiceId>> ListDueAsync(DateOnly today, CancellationToken cancellationToken);

    Task RemoveAsync(RecurringInvoice template, CancellationToken cancellationToken);
}

/// <summary>Une facture créée par un modèle, telle qu'elle est aujourd'hui (numéro et statut).</summary>
public sealed record RecurringInvoiceState(InvoiceId Id, string? Number, InvoiceStatus Status);

public interface IRecurringInvoiceReadStore
{
    Task<PagedResponse<RecurringInvoiceSummary>> ListAsync(RecurringInvoiceListFilter filter, CancellationToken cancellationToken);

    /// <summary>Numéro et statut des factures créées par un modèle ; les brouillons supprimés n'y figurent plus.</summary>
    Task<IReadOnlyDictionary<InvoiceId, RecurringInvoiceState>> InvoicesAsync(IReadOnlyCollection<InvoiceId> ids, CancellationToken cancellationToken);
}
