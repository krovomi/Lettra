using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Routage par rôles (annexe G §6) : une fonction déterministe de la portée, de la gravité et du niveau d'escalade.
/// Vigie n'intervient jamais ici.
/// </summary>
public sealed class RoutingPolicy(IRoleDirectory directory, IOnCallSchedule onCall)
{
    public async Task<IReadOnlyCollection<Recipient>> ResolveAsync(Alert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        HashSet<Recipient> recipients = [];

        if (alert.Scope.IsTechnical)
        {
            recipients.UnionWith(await onCall.CurrentAsync(alert.Category, alert.EscalationLevel, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            recipients.UnionWith(await directory.HoldersAsync(SupervisionPermission.AlertesLire, alert.Scope, cancellationToken).ConfigureAwait(false));
        }

        if (alert.Severity == Severity.S1 && alert.Scope.Cabinet is { } cabinet)
        {
            recipients.UnionWith(await directory.CabinetAdminsAsync(cabinet, cancellationToken).ConfigureAwait(false));
        }

        return recipients;
    }
}
