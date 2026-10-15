using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Centre d'alertes du dossier ouvert. <c>cabinetWide</c> élargit la vue au cabinet de ce dossier, si l'utilisateur en a
/// le droit. Les actions portent sur le cabinet et renvoient l'alerte relue.
/// </summary>
public interface IAlertsGateway
{
    Task<OperationResult<AlertCentreResponse>> ListAsync(bool cabinetWide, bool includeClosed, CancellationToken cancellationToken);

    Task<OperationResult<AlertDetailResponse>> GetAsync(Guid alertId, bool cabinetWide, CancellationToken cancellationToken);

    Task<OperationResult<AlertDetailResponse>> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken);

    Task<OperationResult<AlertDetailResponse>> ResolveAsync(Guid alertId, CancellationToken cancellationToken);

    Task<OperationResult<AlertDetailResponse>> ChangeSeverityAsync(Guid alertId, ChangeAlertSeverityRequest request, CancellationToken cancellationToken);
}
