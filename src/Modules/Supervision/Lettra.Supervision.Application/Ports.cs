using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>Base de supervision de la cellule : des métadonnées seulement, jamais le contenu d'un dossier (annexe G §9).</summary>
public interface IAlertStore
{
    /// <summary>La dernière alerte de cette empreinte, quel que soit son état (une récidive rouvre l'alerte).</summary>
    Task<Alert?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken);

    Task<Alert?> GetAsync(AlertId id, CancellationToken cancellationToken);

    /// <summary>Alertes ouvertes (Nouvelle ou Prise en charge) et résolues non clôturées, pour l'escalade et la clôture.</summary>
    Task<IReadOnlyList<Alert>> ListActiveAsync(CancellationToken cancellationToken);

    Task SaveAsync(Alert alert, CancellationToken cancellationToken);
}

/// <summary>Destinataire résolu par le routage : une personne, ou la rotation d'astreinte de l'éditeur.</summary>
public sealed record Recipient(RecipientKind Kind, string Id);

public enum RecipientKind
{
    User = 1,
    OnCall = 2,
}

/// <summary>Permissions de l'annexe G §6, avec la même logique de portée que les autres (annexe F §3.2).</summary>
public enum SupervisionPermission
{
    AlertesLire = 1,
    AlertesTraiter = 2,
    AlertesConfigurer = 3,
}

/// <summary>« Qui détient telle permission sur tel périmètre ? » : jamais une personne nommée dans une règle.</summary>
public interface IRoleDirectory
{
    Task<IReadOnlyCollection<Recipient>> HoldersAsync(SupervisionPermission permission, AlertScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Recipient>> CabinetAdminsAsync(Guid cabinet, CancellationToken cancellationToken);
}

/// <summary>Planning d'astreinte de l'éditeur (outil d'astreinte hébergé en France, annexe G §12).</summary>
public interface IOnCallSchedule
{
    /// <summary>Qui prévenir pour cette catégorie, au niveau d'escalade donné (0 = premier niveau).</summary>
    Task<IReadOnlyCollection<Recipient>> CurrentAsync(AlertCategory category, int escalationLevel, CancellationToken cancellationToken);
}

/// <summary>
/// Ce qu'une notification contient, et rien d'autre : titre, gravité, portée et lien (annexe G §6).
/// Le détail se lit dans Lettra, avec les droits du destinataire.
/// </summary>
public sealed record AlertNotification(AlertId AlertId, string Title, Severity Severity, ScopeLevel ScopeLevel, Uri Link, int EscalationLevel);

/// <summary>Canal de notification (in-app, e-mail, Teams, Slack, astreinte).</summary>
public interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(AlertNotification notification, IReadOnlyCollection<Recipient> recipients, CancellationToken cancellationToken);
}

/// <summary>Proposition de Vigie : affichée comme « suggestion de l'IA », jamais appliquée seule (annexe G §7).</summary>
public sealed record TriageSuggestion(string Summary, string? ProbableCause, Severity? SuggestedSeverity, string? RunbookId);

/// <summary>
/// Port de l'agent de tri Vigie. Hors du chemin critique : l'ingestion, le routage et l'escalade ne l'appellent
/// jamais. Branché en phase 6 sur l'orchestrateur IA, d'abord en observation.
/// </summary>
public interface IAlertTriageAgent
{
    Task<TriageSuggestion?> SuggestAsync(Alert alert, CancellationToken cancellationToken);
}
