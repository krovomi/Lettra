using Lettra.Contracts.Common;
using Lettra.Contracts.Platform;

namespace Lettra.UI.Abstractions;

/// <summary>
/// L'administration de la plateforme (mode 5) : les cabinets de la cellule, leur arrivée et leur départ, la relance des
/// créations de dossiers en échec. HTTP seulement : un poste isolé n'a pas de plateforme, l'écran ne s'y affiche pas.
/// </summary>
public interface IPlatformCabinetsGateway
{
    Task<OperationResult<PlatformOverviewResponse>> GetOverviewAsync(CancellationToken cancellationToken);

    Task<OperationResult<OnboardCabinetResponse>> OnboardAsync(OnboardCabinetRequest request, CancellationToken cancellationToken);

    Task<OperationResult<PlatformCabinetResponse>> ScheduleDepartureAsync(Guid cabinetId, CancellationToken cancellationToken);

    Task<OperationResult<PlatformCabinetResponse>> CancelDepartureAsync(Guid cabinetId, CancellationToken cancellationToken);

    Task<OperationResult<DossierRetryResponse>> RetryFailedDossiersAsync(CancellationToken cancellationToken);
}
