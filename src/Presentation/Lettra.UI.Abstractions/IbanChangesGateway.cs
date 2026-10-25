using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;

namespace Lettra.UI.Abstractions;

/// <summary>Critères de la liste des changements d'IBAN : à vérifier seulement par défaut, les plus récents en tête.</summary>
public sealed record IbanChangeListQuery(
    IbanChangeStatusDto? Status = IbanChangeStatusDto.Pending,
    string? Search = null,
    int Page = 1,
    int PageSize = 50,
    IbanChangeSortDto Sort = IbanChangeSortDto.ChangedAt,
    SortDirectionDto Direction = SortDirectionDto.Descending);

/// <summary>
/// Changements d'IBAN des fournisseurs, à vérifier par un autre canal avant tout paiement (fraude au faux fournisseur).
/// Lire exige <c>TiersConsulter</c>, décider <c>TiersModifier</c>. Refus possibles : <c>IbanChange.NotFound</c>,
/// <c>IbanChange.AlreadyDecided</c>, <c>IbanChange.MethodRequired</c>, <c>IbanChange.NoteRequired</c>,
/// <c>IbanChange.SameUser</c> (séparation des tâches du cabinet).
/// </summary>
public interface IIbanChangesGateway
{
    Task<OperationResult<PagedResponse<IbanChangeResponse>>> ListAsync(IbanChangeListQuery query, CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<IbanChangeResponse>>> ForSupplierAsync(Guid supplierId, CancellationToken cancellationToken);

    Task<OperationResult<bool>> ConfirmAsync(Guid changeId, ConfirmIbanChangeRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> RejectAsync(Guid changeId, RejectIbanChangeRequest request, CancellationToken cancellationToken);
}
