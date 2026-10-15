using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Mapping;
using Lettra.Contracts.Alerts;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Mêmes cas d'usage et mêmes portées que les routes <c>/alerts</c> du dossier de l'API.</summary>
internal sealed class InProcessAlertsGateway(IServiceScopeFactory scopes) : IAlertsGateway
{
    public Task<OperationResult<AlertCentreResponse>> ListAsync(bool cabinetWide, bool includeClosed, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, _, ct) =>
            InProcessScope.QueryAsync(sp, new ListAlertsQuery(Within(sp, cabinetWide), includeClosed), ct), cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> GetAsync(Guid alertId, bool cabinetWide, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, _, ct) =>
            InProcessScope.QueryAsync(sp, new GetAlertQuery(new AlertId(alertId), Within(sp, cabinetWide)), ct), cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> AcknowledgeAsync(Guid alertId, CancellationToken cancellationToken) =>
        ActAsync(sp => new AcknowledgeAlertCommand(new AlertId(alertId), Within(sp, cabinetWide: true)), cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> ResolveAsync(Guid alertId, CancellationToken cancellationToken) =>
        ActAsync(sp => new ResolveAlertCommand(new AlertId(alertId), Within(sp, cabinetWide: true)), cancellationToken);

    public Task<OperationResult<AlertDetailResponse>> ChangeSeverityAsync(Guid alertId, ChangeAlertSeverityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(sp => new ChangeAlertSeverityCommand(new AlertId(alertId), (Severity)request.Severity, request.Reason, Within(sp, cabinetWide: true)),
            cancellationToken);
    }

    private Task<OperationResult<AlertDetailResponse>> ActAsync(Func<IServiceProvider, AlertActionCommand> command, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, async (sp, _, ct) =>
        {
            AlertActionCommand action = command(sp);
            ICommandDispatcher commands = sp.GetRequiredService<ICommandDispatcher>();
            Result result = action switch
            {
                AcknowledgeAlertCommand acknowledge => await commands.SendAsync(acknowledge, ct).ConfigureAwait(false),
                ResolveAlertCommand resolve => await commands.SendAsync(resolve, ct).ConfigureAwait(false),
                ChangeAlertSeverityCommand change => await commands.SendAsync(change, ct).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
            return result.IsFailure
                ? OperationResult<AlertDetailResponse>.Failure(result.Errors.ToErrorDetails())
                : await InProcessScope.QueryAsync(sp, new GetAlertQuery(action.AlertId, action.Within), ct).ConfigureAwait(false);
        }, cancellationToken);

    private static AuthorizationScope Within(IServiceProvider services, bool cabinetWide)
    {
        TenantDescriptor current = services.GetRequiredService<ITenantContext>().Current;
        return cabinetWide ? AuthorizationScope.ForCabinet(current.OrganisationId) : AuthorizationScope.ForDossier(current.DossierId);
    }
}
