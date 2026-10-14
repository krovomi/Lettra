using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Domain.Common;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Un cas d'usage du centre d'alertes, avec sa permission et sa portée (annexe G §6). Tant que l'autorisation à portée
/// (chantier Identité) ne sait pas lire cette interface, le pipeline refuse ces commandes : le défaut est fermé.
/// </summary>
public interface IRequireAlertPermission
{
    SupervisionPermission Permission { get; }

    AlertScope Scope { get; }
}

public sealed record AcknowledgeAlertCommand(AlertId AlertId, AlertScope Scope) : ICommand<Result>, IRequireAlertPermission
{
    public SupervisionPermission Permission => SupervisionPermission.AlertesTraiter;
}

/// <summary>Changer la gravité : motif obligatoire et tracé (annexe G §5).</summary>
public sealed record ChangeAlertSeverityCommand(AlertId AlertId, Severity NewSeverity, string Reason, AlertScope Scope)
    : ICommand<Result>, IRequireAlertPermission
{
    public SupervisionPermission Permission => SupervisionPermission.AlertesTraiter;
}

public sealed record ResolveAlertCommand(AlertId AlertId, AlertScope Scope) : ICommand<Result>, IRequireAlertPermission
{
    public SupervisionPermission Permission => SupervisionPermission.AlertesTraiter;
}

internal sealed class AlertCommandHandlers(IAlertStore store, ICurrentUser user, TimeProvider clock) :
    ICommandHandler<AcknowledgeAlertCommand, Result>,
    ICommandHandler<ChangeAlertSeverityCommand, Result>,
    ICommandHandler<ResolveAlertCommand, Result>
{
    public Task<Result> HandleAsync(AcknowledgeAlertCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.AlertId, command.Scope, (alert, actor, now) => alert.Acknowledge(actor, now), cancellationToken);

    public Task<Result> HandleAsync(ChangeAlertSeverityCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.AlertId, command.Scope, (alert, actor, now) => alert.ChangeSeverity(command.NewSeverity, command.Reason, actor, now), cancellationToken);

    public Task<Result> HandleAsync(ResolveAlertCommand command, CancellationToken cancellationToken) =>
        ApplyAsync(command.AlertId, command.Scope, (alert, actor, now) => alert.Resolve(actor, now), cancellationToken);

    private async Task<Result> ApplyAsync(AlertId id, AlertScope scope, Func<Alert, Actor, DateTimeOffset, Result> action, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return SecurityErrors.Unauthenticated;
        }

        // La portée déclarée doit être celle de l'alerte : on n'agit pas hors du périmètre vérifié par l'autorisation.
        Alert? alert = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (alert is null || alert.Scope != scope)
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
