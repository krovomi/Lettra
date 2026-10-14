using Lettra.Domain.Common;
using Lettra.Supervision.Domain;
using Microsoft.Extensions.Logging;

namespace Lettra.Supervision.Application;

/// <summary>
/// Entrée unique des signaux (webhook Alertmanager aujourd'hui, règles métier en phase 3) : dédoublonnage par empreinte,
/// réouverture sur récidive, résolution quand le signal disparaît, puis routage et notification d'une alerte nouvelle.
/// </summary>
public sealed partial class AlertIngestion(
    IAlertStore store,
    RoutingPolicy routing,
    IEnumerable<INotificationChannel> channels,
    SupervisionLinks links,
    ILogger<AlertIngestion> logger)
{
    public async Task<Result<AlertId>> IngestAsync(AlertSignal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        string fingerprint = Alert.FingerprintOf(signal.RuleId, signal.Scope);
        Alert? existing = await store.FindByFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false);

        if (!signal.Firing)
        {
            if (existing is null || existing.State is AlertState.Resolved or AlertState.Closed)
            {
                return existing?.Id is { } id ? id : Result<AlertId>.Failure(SupervisionErrors.NotFound);
            }

            Result resolved = existing.Resolve(Actor.Rule(signal.RuleId), signal.At);
            if (resolved.IsFailure)
            {
                return Result<AlertId>.Failure(resolved.Errors);
            }

            await store.SaveAsync(existing, cancellationToken).ConfigureAwait(false);
            LogResolved(logger, signal.RuleId, existing.Id.Value);
            return existing.Id;
        }

        Alert alert;
        if (existing is null)
        {
            Result<Alert> raised = Alert.Raise(signal.RuleId, signal.Title, signal.Severity, signal.Category, signal.Scope, signal.At);
            if (raised.IsFailure)
            {
                return Result<AlertId>.Failure(raised.Errors);
            }

            alert = raised.Value;
        }
        else
        {
            alert = existing;
            alert.RecordOccurrence(signal.At);
        }

        bool mustNotify = alert.DomainEvents.OfType<AlertRaised>().Any();
        alert.ClearDomainEvents();
        await store.SaveAsync(alert, cancellationToken).ConfigureAwait(false);

        if (mustNotify)
        {
            await NotifyAsync(alert, cancellationToken).ConfigureAwait(false);
        }

        return alert.Id;
    }

    internal async Task NotifyAsync(Alert alert, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Recipient> recipients = await routing.ResolveAsync(alert, cancellationToken).ConfigureAwait(false);
        AlertNotification notification = new(alert.Id, alert.Title, alert.Severity, alert.Scope.Level, links.For(alert.Id), alert.EscalationLevel);
        foreach (INotificationChannel channel in channels)
        {
            await channel.SendAsync(notification, recipients, cancellationToken).ConfigureAwait(false);
        }

        LogNotified(logger, alert.RuleId, alert.Id.Value, alert.Severity.ToString(), recipients.Count);
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Alerte {RuleId} ({AlertId}) notifiée en {Severity} à {RecipientCount} destinataire(s)")]
    private static partial void LogNotified(ILogger logger, string ruleId, Guid alertId, string severity, int recipientCount);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Alerte {RuleId} ({AlertId}) résolue par sa règle")]
    private static partial void LogResolved(ILogger logger, string ruleId, Guid alertId);
}
