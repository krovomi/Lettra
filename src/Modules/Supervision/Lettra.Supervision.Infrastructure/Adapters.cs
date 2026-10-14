using System.Collections.Concurrent;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Supervision.Infrastructure;

/// <summary>
/// Stockage en mémoire du squelette. La base de supervision par cellule (métadonnées seulement, séparée par cabinet)
/// la remplace en phase 3 avec le centre d'alertes.
/// </summary>
internal sealed class InMemoryAlertStore : IAlertStore
{
    private readonly ConcurrentDictionary<AlertId, Alert> _alerts = new();

    public Task<Alert?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken) =>
        Task.FromResult(_alerts.Values.Where(a => a.Fingerprint == fingerprint).MaxBy(a => a.LastSeen));

    public Task<Alert?> GetAsync(AlertId id, CancellationToken cancellationToken) =>
        Task.FromResult(_alerts.GetValueOrDefault(id));

    public Task<IReadOnlyList<Alert>> ListActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Alert>>([.. _alerts.Values.Where(a => a.State != AlertState.Closed)]);

    public Task SaveAsync(Alert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        _alerts[alert.Id] = alert;
        return Task.CompletedTask;
    }
}

/// <summary>Astreinte lue dans la configuration : un identifiant de rotation par niveau d'escalade.</summary>
internal sealed class ConfiguredOnCallSchedule(IOptions<SupervisionOptions> options) : IOnCallSchedule
{
    public Task<IReadOnlyCollection<Recipient>> CurrentAsync(AlertCategory category, int escalationLevel, CancellationToken cancellationToken)
    {
        IList<string> levels = options.Value.OnCallLevels;
        IReadOnlyCollection<Recipient> recipients = levels.Count == 0
            ? []
            : [new Recipient(RecipientKind.OnCall, levels[Math.Min(escalationLevel, levels.Count - 1)])];
        return Task.FromResult(recipients);
    }
}

/// <summary>
/// Annuaire des rôles en attente du chantier Identité (rôles à portée, ADR-0010) : personne côté cabinet pour l'instant.
/// Les alertes de cabinet et de dossier arrivent en phase 3, avec l'annuaire réel.
/// </summary>
internal sealed class PendingRoleDirectory : IRoleDirectory
{
    public Task<IReadOnlyCollection<Recipient>> HoldersAsync(SupervisionPermission permission, AlertScope scope, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Recipient>>([]);

    public Task<IReadOnlyCollection<Recipient>> CabinetAdminsAsync(Guid cabinet, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Recipient>>([]);
}

/// <summary>Canal « journal technique » : trace l'envoi (titre, gravité, nombre de destinataires), rien d'autre.</summary>
internal sealed partial class LogNotificationChannel(ILogger<LogNotificationChannel> logger) : INotificationChannel
{
    public string Name => "journal";

    public Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(recipients);
        LogSent(logger, notification.Severity.ToString(), notification.Title, notification.ScopeLevel.ToString(), recipients.Count, notification.EscalationLevel);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Warning, Message = "Notification {Severity} « {Title} » (portée {ScopeLevel}, escalade {EscalationLevel}) vers {RecipientCount} destinataire(s)")]
    private static partial void LogSent(ILogger logger, string severity, string title, string scopeLevel, int recipientCount, int escalationLevel);
}
