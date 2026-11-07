using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Les dossiers du cabinet, gérés par son administrateur : la liste, la création, l'effacement (annulable pendant le
/// délai de rétractation). In-process sur le poste isolé (ses dossiers ne s'y effacent pas), HTTP ailleurs.
/// </summary>
public interface ICabinetDossiersGateway
{
    Task<OperationResult<IReadOnlyList<CabinetDossierResponse>>> ListAsync(Guid cabinetId, CancellationToken cancellationToken);

    Task<OperationResult<DossierSummary>> CreateAsync(Guid cabinetId, CreateDossierRequest request, CancellationToken cancellationToken);

    Task<OperationResult<DossierErasureResponse>> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken);

    Task<OperationResult<DossierSummary>> CancelErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken);
}
