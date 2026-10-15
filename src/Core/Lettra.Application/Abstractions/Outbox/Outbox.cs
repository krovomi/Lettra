namespace Lettra.Application.Abstractions.Outbox;

/// <summary>
/// Outbox transactionnelle du serveur (étape 5.4) : un envoi vers l'extérieur (plateforme agréée, déclaration
/// d'encaissement) est d'abord écrit dans la base du dossier, dans la même transaction que le changement métier qui le
/// décide. Un répartiteur l'envoie ensuite, avec reprises, jusqu'à ce qu'il parte ou soit mis de côté.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Inscrit le message dans le contexte de la requête : il est écrit par le prochain <c>SaveChanges</c> du cas
    /// d'usage, ou pas du tout. Une même clé d'idempotence ne donne qu'un message, dont l'identifiant est rendu.
    /// </summary>
    Task<Guid> EnqueueAsync(string kind, string idempotencyKey, string payload, CancellationToken cancellationToken);
}

/// <summary>Un message à envoyer, tel que son gestionnaire le reçoit.</summary>
/// <param name="Id">Identifiant du message dans l'outbox du dossier.</param>
/// <param name="Kind">Type du message, qui désigne son gestionnaire.</param>
/// <param name="IdempotencyKey">
/// À transmettre au destinataire : un message peut être livré plus d'une fois (arrêt après l'envoi, avant la
/// confirmation), le destinataire ou le gestionnaire reconnaît alors l'envoi déjà fait.
/// </param>
/// <param name="Payload">Contenu du message (JSON), écrit par le cas d'usage.</param>
/// <param name="Attempt">Numéro de l'essai, à partir de 1.</param>
/// <param name="EnqueuedAt">Instant de la mise en file.</param>
public sealed record OutboxDelivery(Guid Id, string Kind, string IdempotencyKey, string Payload, int Attempt, DateTimeOffset EnqueuedAt);

public enum OutboxResult
{
    Delivered = 0,

    /// <summary>Échec passager (destinataire injoignable) : nouvel essai plus tard.</summary>
    Retry = 1,

    /// <summary>Le message ne partira jamais tel quel : mis de côté sans nouvel essai.</summary>
    Poison = 2,
}

public readonly record struct OutboxOutcome(OutboxResult Result, string? Reason)
{
    public static OutboxOutcome Delivered => new(OutboxResult.Delivered, null);

    public static OutboxOutcome Retry(string reason) => new(OutboxResult.Retry, reason);

    public static OutboxOutcome Poison(string reason) => new(OutboxResult.Poison, reason);
}

/// <summary>
/// Envoie les messages d'un type. Il s'exécute dans une portée neuve du dossier et peut y enregistrer l'effet de
/// l'envoi (accusé de la plateforme) ; une exception vaut un échec passager.
/// </summary>
public interface IOutboxHandler
{
    string Kind { get; }

    Task<OutboxOutcome> HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>Bilan d'une passe du répartiteur sur un dossier.</summary>
public sealed record OutboxPass(int Delivered, int Retried, int Parked);

/// <summary>État d'un message, pour le suivi et les tests.</summary>
public sealed record OutboxMessageState(
    Guid Id,
    string Kind,
    string IdempotencyKey,
    int Attempts,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ParkedAt,
    string? LastError);

/// <summary>
/// Répartiteur de l'outbox du dossier courant. Chaque message est pris sous bail : deux répartiteurs (le Worker et
/// l'API, ou deux Workers) ne l'envoient pas en même temps, et celui d'un processus arrêté en plein envoi est repris à
/// l'expiration du bail.
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>Envoie les messages dus du dossier, les plus anciens d'abord (passe du Worker).</summary>
    Task<OutboxPass> DispatchDueAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Le chemin synchrone : envoie tout de suite un message que le cas d'usage vient d'écrire, sans attendre le Worker.
    /// Rend <c>null</c> si l'envoi immédiat est désactivé ou si le message n'est pas à envoyer maintenant (déjà parti, déjà
    /// pris par un autre répartiteur, en attente de son prochain essai).
    /// </summary>
    Task<OutboxOutcome?> DispatchNowAsync(Guid messageId, CancellationToken cancellationToken);

    Task<OutboxMessageState?> FindAsync(Guid messageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxMessageState>> ListParkedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Avance au plus tôt l'essai d'un message pas encore parti : mis de côté (ses essais repartent de zéro, après
    /// correction) ou en attente de son prochain essai (l'utilisateur redemande l'envoi). Faux s'il est parti ou en cours.
    /// </summary>
    Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken);
}

/// <summary>Réglages de l'outbox (section <c>Lettra:Outbox</c>).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Lettra:Outbox";

    /// <summary>Essais avant de mettre un message de côté.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Attente après le premier échec ; elle double à chaque échec suivant.</summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Durée du bail d'un envoi : au-delà, un répartiteur arrêté en plein envoi est relayé.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Messages envoyés au plus par dossier et par passe.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Envoi immédiat par la requête qui écrit le message (vrai par défaut) : rien ne change quand aucun Worker ne tourne.
    /// Faux en SaaS si l'on veut que seul le Worker envoie.
    /// </summary>
    public bool DispatchInline { get; set; } = true;

    /// <summary>Attente après l'échec numéro <paramref name="failures"/> (à partir de 1).</summary>
    public TimeSpan RetryDelay(int failures)
    {
        int doublings = Math.Clamp(failures - 1, 0, 30);
        double seconds = FirstRetryDelay.TotalSeconds * Math.Pow(2, doublings);
        return seconds >= MaxRetryDelay.TotalSeconds ? MaxRetryDelay : TimeSpan.FromSeconds(seconds);
    }
}
