using Lettra.Contracts.Common;

namespace Lettra.UI.Abstractions;

// Synchronisation du poste vue par l'UI (étape 4.3, ADR-0008). Ces types reprennent ceux de Lettra.Sync.Client, que
// l'UI ne voit pas : l'hôte d'un poste qui synchronise (MAUI, modes 2 à 5) fournit un adaptateur. Sans adaptateur
// (navigateur, poste isolé), aucun ISyncCenter n'est enregistré et l'UI n'affiche rien de la synchronisation.

/// <summary>Où en est la synchronisation (miroir de <c>SyncState</c>).</summary>
public enum SyncStatus
{
    Offline = 1,
    PendingConsent = 2,
    Pushing = 3,
    Pulling = 4,
    ResolvingConflicts = 5,
    UpToDate = 6,
    Failed = 7,
}

/// <summary>Pourquoi le dernier cycle a échoué (miroir de <c>SyncFailure</c>).</summary>
public enum SyncFailureKind
{
    None = 0,
    ConnectionLost = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    DossierUnavailable = 4,

    /// <summary>Le cabinet a révoqué ce poste : sa réplique est effacée (étape 4.4).</summary>
    DeviceRevoked = 5,
}

/// <summary>Rappel quand une facture validée sur le poste attend d'être envoyée (miroir de <c>SyncReminder</c>).</summary>
public enum SyncReminderLevel
{
    None = 0,

    /// <summary>Depuis plus de 24 h.</summary>
    Day = 1,

    /// <summary>Depuis plus de 72 h.</summary>
    ThreeDays = 2,
}

/// <summary>Préférence de l'utilisateur (miroir de <c>SyncPreference</c>).</summary>
public enum SyncMode
{
    /// <summary>Défaut : on demande à chaque retour du réseau.</summary>
    AlwaysAsk = 1,

    /// <summary>Sans demander en Wi-Fi ou Ethernet ; on demande en réseau mobile.</summary>
    AutoOnWifi = 2,

    /// <summary>Jamais en réseau mobile (ni envoi, ni question) ; on demande ailleurs.</summary>
    NeverOnCellular = 3,
}

/// <summary>Réseau utilisé (miroir de <c>NetworkKind</c>).</summary>
public enum SyncNetwork
{
    None = 0,
    Wifi = 1,
    Ethernet = 2,
    Cellular = 3,
}

/// <summary>Réponse à la demande (miroir de <c>SyncConsent</c>).</summary>
public enum SyncAnswer
{
    Now = 1,
    Later = 2,
    ShowDetail = 3,
}

/// <summary>Ce qui partirait : « 2 factures validées, 2 créations, 5 modifications ».</summary>
public sealed record SyncPendingSummary(int Creations, int Changes, long EstimatedBytes, int Validations)
{
    public static SyncPendingSummary Empty { get; } = new(0, 0, 0, 0);

    public int Total => Creations + Changes + Validations;
}

/// <summary>Nature de l'élément saisi sur le poste.</summary>
public enum SyncItemKind
{
    Customer = 1,
    Supplier = 2,
    CatalogItem = 3,
    Invoice = 4,
}

/// <summary>Ce que l'intention fait de l'élément.</summary>
public enum SyncChangeKind
{
    Creation = 1,
    Change = 2,

    /// <summary>Facture validée hors ligne, avec son numéro de la série du poste (ADR-0013).</summary>
    Validation = 3,
}

/// <summary>
/// Une saisie du poste qui attend de partir, pour le détail de la demande. <c>Label</c> est ce que l'utilisateur
/// reconnaît : nom du client, désignation, numéro de facture.
/// </summary>
public sealed record SyncPendingItem(long Sequence, SyncItemKind Item, SyncChangeKind Change, Guid EntityId, string? Label, DateTimeOffset RecordedAt);

/// <summary>
/// Un champ modifié à la fois sur le poste et au cabinet. <c>Id</c> identifie le conflit (une intention,
/// <c>IdempotencyKey</c>, peut en porter plusieurs, un par champ). <c>Field</c>
/// est le nom de la propriété du contrat (ex. <c>Email</c>) ; <c>Base</c>, <c>Mine</c> et <c>Server</c> sont la valeur
/// d'avant, la saisie du poste et celle du cabinet, prêtes à afficher.
/// </summary>
public sealed record SyncFieldDispute(
    long Id,
    Guid IdempotencyKey,
    SyncItemKind Item,
    Guid EntityId,
    string? Label,
    string Field,
    string? Base,
    string? Mine,
    string? Server,
    DateTimeOffset DetectedAt);

/// <summary>
/// Une saisie refusée par le serveur (règles, droits, facture hors séquence), avec ses codes de
/// <see cref="Contracts.Common.ErrorCodes"/> : affichée en lecture seule, ce n'est pas un conflit de champ.
/// </summary>
public sealed record SyncRejectedItem(long Sequence, SyncItemKind Item, SyncChangeKind Change, Guid EntityId, string? Label, IReadOnlyList<string> ErrorCodes);

/// <summary>Compte rendu d'un cycle (miroir de <c>SyncReport</c>).</summary>
public sealed record SyncCycleReport(
    SyncStatus Status,
    SyncFailureKind Failure,
    SyncPendingSummary Summary,
    int Sent,
    int Conflicts,
    int Rejected,
    int Received,
    int KeptLocal,
    bool Postponed,
    DateTimeOffset FinishedAt);

/// <summary>
/// Ce que la pastille et l'écran de synchronisation affichent : <c>Conflicts</c> compte les champs disputés en attente,
/// <c>Rejected</c> les saisies refusées, et <c>LastReport</c> est <c>null</c> avant le premier cycle.
/// </summary>
public sealed record SyncSnapshot(
    SyncStatus Status,
    SyncPendingSummary Pending,
    SyncReminderLevel Reminder,
    int Conflicts,
    int Rejected,
    SyncCycleReport? LastReport)
{
    public static SyncSnapshot Initial { get; } = new(SyncStatus.Offline, SyncPendingSummary.Empty, SyncReminderLevel.None, 0, 0, null);
}

/// <summary>Choix pour un conflit (un champ) : la saisie du poste, ou la valeur du cabinet.</summary>
public sealed record SyncFieldChoice(long ConflictId, bool KeepMine);

/// <summary>
/// Synchronisation du poste pour l'UI. Implémentée par l'adaptateur de <c>SyncCoordinator</c> ; absente du conteneur
/// quand l'hôte ne synchronise pas.
/// </summary>
public interface ISyncCenter
{
    SyncSnapshot Current { get; }

    SyncMode Mode { get; }

    /// <summary>L'état, le résumé, le rappel ou la préférence ont changé (peut venir d'un autre thread).</summary>
    event EventHandler? Changed;

    /// <summary>« Synchroniser maintenant » : un cycle complet ; la demande s'affiche si la préférence l'exige.</summary>
    Task<SyncCycleReport> SyncNowAsync(CancellationToken cancellationToken);

    Task SetModeAsync(SyncMode mode, CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncPendingItem>> ListPendingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncFieldDispute>> ListConflictsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncRejectedItem>> ListRejectedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Résout des conflits, un choix par champ (l'écran envoie ensemble ceux d'une même saisie). « Poste » renvoie la
    /// saisie dans une nouvelle modification sur la version du cabinet ; « cabinet » la laisse. Refus possibles :
    /// <c>Sync.EntityNotFound</c>, <c>Sync.ConflictAwaitingRefresh</c> (synchroniser d'abord).
    /// </summary>
    Task<OperationResult<bool>> ResolveAsync(IReadOnlyList<SyncFieldChoice> choices, CancellationToken cancellationToken);
}

/// <summary>
/// Demande non bloquante à l'utilisateur (miroir de <c>ISyncConsentPrompt</c>), implémentée par l'UI : l'adaptateur la
/// relaie au <c>SyncCoordinator</c>.
/// </summary>
public interface ISyncConsentDialog
{
    Task<SyncAnswer> AskAsync(SyncPendingSummary summary, SyncNetwork network, CancellationToken cancellationToken);

    Task ShowDetailAsync(IReadOnlyList<SyncPendingItem> pending, CancellationToken cancellationToken);
}
