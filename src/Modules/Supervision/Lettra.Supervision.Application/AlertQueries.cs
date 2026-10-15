using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Alerts;
using Lettra.Domain.Common;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Le centre d'alertes d'une portée (cabinet, groupe ou dossier) : <c>AlertesLire</c> sur cette portée suffit, car
/// les droits descendent du cabinet vers ses groupes et ses dossiers. Un collaborateur consulte donc son dossier, un
/// manager tout le cabinet.
/// </summary>
public sealed record ListAlertsQuery(AuthorizationScope Within, bool IncludeClosed = false)
    : IQuery<Result<AlertCentreResponse>>, IRequireScopedPermission
{
    public const int MaxAlerts = 500;

    public Permission Permission => Permission.AlertesLire;

    public AuthorizationScope Scope => Within;
}

public sealed record GetAlertQuery(AlertId AlertId, AuthorizationScope Within) : IQuery<Result<AlertDetailResponse>>, IRequireScopedPermission
{
    public Permission Permission => Permission.AlertesLire;

    public AuthorizationScope Scope => Within;
}

internal sealed class AlertQueryHandlers(IAlertStore store, EscalationPolicy escalation) :
    IQueryHandler<ListAlertsQuery, Result<AlertCentreResponse>>,
    IQueryHandler<GetAlertQuery, Result<AlertDetailResponse>>
{
    public async Task<Result<AlertCentreResponse>> HandleAsync(ListAlertsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<Alert> alerts = await store.ListWithinAsync(query.Within, query.IncludeClosed, ListAlertsQuery.MaxAlerts, cancellationToken)
            .ConfigureAwait(false);
        return Result<AlertCentreResponse>.Success(AlertMapper.ToCentre(alerts, escalation));
    }

    public async Task<Result<AlertDetailResponse>> HandleAsync(GetAlertQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Alert? alert = await store.GetAsync(query.AlertId, cancellationToken).ConfigureAwait(false);
        return alert is null || !AlertScopes.IsWithin(alert.Scope, query.Within)
            ? Result<AlertDetailResponse>.Failure(SupervisionErrors.NotFound)
            : AlertMapper.ToDetail(alert, escalation);
    }
}
