using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>Filtre et tri de la liste des modèles récurrents.</summary>
public sealed record RecurringInvoiceListQuery(
    string? Search = null,
    Guid? CustomerId = null,
    bool? Active = null,
    bool DueOnly = false,
    RecurringInvoiceSortDto Sort = RecurringInvoiceSortDto.NextIssueDate,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

/// <summary>
/// Factures récurrentes (vague 7). Lecture : <c>DossierLire</c> ; modèles et création des factures dues :
/// <c>FactureSaisir</c> ; la validation automatique demande en plus <c>FactureValider</c> à celui qui lance la création.
/// </summary>
public interface IRecurringInvoicesGateway
{
    Task<OperationResult<PagedResponse<RecurringInvoiceSummary>>> ListAsync(RecurringInvoiceListQuery query, CancellationToken cancellationToken);

    Task<OperationResult<RecurringInvoiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<RecurringInvoiceResponse>> CreateAsync(SaveRecurringInvoiceRequest request, CancellationToken cancellationToken);

    Task<OperationResult<RecurringInvoiceResponse>> UpdateAsync(Guid id, SaveRecurringInvoiceRequest request, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<RecurringInvoiceResponse>> SetActiveAsync(Guid id, bool isActive, long? expectedVersion, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteAsync(Guid id, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Crée les factures dues de tous les modèles actifs, ou d'un seul si <paramref name="id"/> est donné.</summary>
    Task<OperationResult<RecurringRunResponse>> RunAsync(Guid? id, CancellationToken cancellationToken);
}
