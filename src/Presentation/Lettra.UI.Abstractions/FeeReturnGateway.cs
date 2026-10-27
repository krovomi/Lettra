using Lettra.Contracts.Common;
using Lettra.Contracts.Tax;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Déclaration des honoraires (DAS 2). Lire demande <c>DossierLire</c> ; suivre ou retirer un bénéficiaire, <c>FecExporter</c>.
/// Refus possibles : <c>Fee.*</c>, <c>Concurrency.VersionMismatch</c>.
/// </summary>
public interface IFeeReturnGateway
{
    /// <summary>L'exercice qui contient <paramref name="date"/>, ou le dernier exercice clos.</summary>
    Task<OperationResult<FeeReturnResponse>> GetAsync(DateOnly? date, CancellationToken cancellationToken);

    /// <summary>Suit le fournisseur (<paramref name="expectedVersion"/> vide) ou met à jour son suivi à partir de la version lue.</summary>
    Task<OperationResult<FeeReturnResponse>> SaveAsync(Guid supplierId, SaveFeeBeneficiaryRequest request, long? expectedVersion, CancellationToken cancellationToken);

    /// <summary>Ne suit plus le fournisseur ; relit l'exercice qui commence le <paramref name="fiscalYearStart"/>.</summary>
    Task<OperationResult<FeeReturnResponse>> RemoveAsync(Guid supplierId, DateOnly fiscalYearStart, long expectedVersion, CancellationToken cancellationToken);
}
