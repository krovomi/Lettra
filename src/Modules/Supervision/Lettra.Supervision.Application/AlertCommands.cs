using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Domain.Common;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Les actions du centre d'alertes portent une portée consultée (<see cref="Within"/>) : le pipeline y vérifie
/// <c>AlertesTraiter</c>, puis le cas d'usage refuse une alerte située hors de cette portée (annexe G §6).
/// </summary>
public abstract record AlertActionCommand(AlertId AlertId, AuthorizationScope Within) : ICommand<Result>, IRequireScopedPermission
{
    public Permission Permission => Permission.AlertesTraiter;

    public AuthorizationScope Scope => Within;
}

public sealed record AcknowledgeAlertCommand(AlertId AlertId, AuthorizationScope Within) : AlertActionCommand(AlertId, Within);

/// <summary>Changer la gravité : motif obligatoire et tracé (annexe G §5).</summary>
public sealed record ChangeAlertSeverityCommand(AlertId AlertId, Severity NewSeverity, string Reason, AuthorizationScope Within)
    : AlertActionCommand(AlertId, Within);

public sealed record ResolveAlertCommand(AlertId AlertId, AuthorizationScope Within) : AlertActionCommand(AlertId, Within);

internal sealed class AlertCommandHandlers(IAlertStore store, ICurrentUser user, TimeProvider clock) :
    ICommandHandler<AcknowledgeAlertCommand, Result>,
    ICommandHandler<ChangeAlertSeverityCommand, Result>,
    ICommandHandler<ResolveAlertCommand, Result>
{
    public Task<Result> HandleAsync(AcknowledgeAlertCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command, (alert, actor, now) => alert.Acknowledge(actor, now), cancellationToken);

    public Task<Result> HandleAsync(ChangeAlertSeverityCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command, (alert, actor, now) => alert.ChangeSeverity(command.NewSeverity, command.Reason, actor, now), cancellationToken);

    public Task<Result> HandleAsync(ResolveAlertCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command, (alert, actor, now) => alert.Resolve(actor, now), cancellationToken);

    private async Task<Result> ApplyAsync(AlertActionCommand command, Func<Alert, Actor, DateTimeOffset, Result> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        Alert? alert = await store.GetAsync(command.AlertId, cancellationToken).ConfigureAwait(false);
        if (alert is null || !AlertScopes.IsWithin(alert.Scope, command.Within))
        {
            return SupervisionErrors.NotFound;
        }

        Result result = action(alert, Actor.Human(user.UserId), clock.GetUtcNow());
        if (result.IsSuccess)
        {
            await store.SaveAsync(alert, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
