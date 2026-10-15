using Lettra.Contracts.Sync;
using Lettra.Domain.Common;

namespace Lettra.Application.Abstractions.Sync;

/// <summary>
/// Intentions préparées pendant une commande, sur un poste qui synchronise (ADR-0008). L'enregistrement de la
/// commande (<c>IUnitOfWork</c>) les écrit dans l'outbox du dossier dans la même transaction : une commande enregistrée
/// a toujours son intention, une commande refusée n'en laisse aucune. Une instance par portée.
/// </summary>
public sealed class SyncOutboxStaging
{
    private readonly List<SyncIntent> _staged = [];

    public bool HasStaged => _staged.Count > 0;

    public void Stage(SyncIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        _staged.Add(intent);
    }

    /// <summary>La commande a échoué : rien ne part.</summary>
    public void Discard() => _staged.Clear();

    /// <summary>Remet les intentions préparées à l'enregistrement, qui les écrit avec la commande.</summary>
    public IReadOnlyList<SyncIntent> TakeAll()
    {
        SyncIntent[] taken = [.. _staged];
        _staged.Clear();
        return taken;
    }
}

/// <summary>État d'une intention dans l'outbox du poste.</summary>
public enum SyncOutboxStatus
{
    /// <summary>Pas encore acquittée par le serveur : elle repartira, avec la même clé.</summary>
    Pending = 1,

    /// <summary>Acquittée : appliquée, fusionnée ou déjà connue.</summary>
    Sent = 2,

    /// <summary>Acquittée avec des champs disputés, qui attendent l'écran de résolution.</summary>
    Conflict = 3,

    /// <summary>Refusée par les règles ou les droits du serveur.</summary>
    Rejected = 4,
}

/// <summary>Une intention de l'outbox, avec la réponse du serveur une fois acquittée.</summary>
/// <param name="Sequence">Ordre de saisie sur le poste ; les intentions partent dans cet ordre.</param>
/// <param name="Intent">L'intention telle que saisie, jamais réécrite.</param>
/// <param name="Status">Où elle en est.</param>
/// <param name="Detail">Codes d'erreur du serveur, si elle a été refusée.</param>
public sealed record SyncOutboxItem(long Sequence, SyncIntent Intent, SyncOutboxStatus Status, string? Detail);

/// <summary>Un champ disputé gardé sur le poste jusqu'à sa résolution (écran de l'étape 4.3).</summary>
public sealed record SyncConflictRecord(
    Guid IdempotencyKey,
    SyncEntityKind Entity,
    Guid EntityId,
    SyncFieldConflict Conflict,
    DateTimeOffset DetectedAt);

/// <summary>Ce qu'a donné l'application des changements reçus sur la réplique.</summary>
/// <param name="Applied">Éléments créés ou mis à jour.</param>
/// <param name="KeptLocal">Éléments laissés tels quels : une saisie du poste n'est pas encore partie.</param>
/// <param name="Failed">Éléments que la réplique n'a pas pu reprendre (règle du domaine changée entre-temps).</param>
public sealed record SyncApplyReport(int Applied, int KeptLocal, int Failed);

/// <summary>Outbox du dossier ouvert sur le poste.</summary>
public interface ISyncOutbox
{
    /// <summary>Intentions dans l'ordre de saisie ; toutes si <paramref name="status"/> est <c>null</c>.</summary>
    Task<IReadOnlyList<SyncOutboxItem>> ListAsync(SyncOutboxStatus? status, CancellationToken cancellationToken);

    /// <summary>
    /// Note les réponses du serveur et garde les champs disputés, en une transaction. Une intention sans réponse reste
    /// en attente et repartira avec la même clé.
    /// </summary>
    Task AcknowledgeAsync(IReadOnlyList<SyncIntentResult> results, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Champs disputés en attente de résolution.</summary>
    Task<IReadOnlyList<SyncConflictRecord>> ListConflictsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Réplique locale du dossier : reçoit l'état du serveur sans passer par les commandes (ce sont des faits du serveur,
/// pas des saisies du poste : aucune intention n'en naît). Un élément qui a une intention en attente garde sa saisie locale.
/// </summary>
public interface ISyncReplica
{
    /// <summary>Jeton de la dernière réception, <c>null</c> avant la première (le serveur envoie alors un instantané).</summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Applique une page de changements et garde son jeton, en une transaction : une coupure fait seulement rejouer la page.</summary>
    Task<SyncApplyReport> ApplyAsync(SyncChangesResponse changes, CancellationToken cancellationToken);

    /// <summary>La série ouverte de ce poste pour <paramref name="baseSeries"/>, avec son prochain numéro local.</summary>
    Task<DeviceSeries?> FindDeviceSeriesAsync(string baseSeries, CancellationToken cancellationToken);

    /// <summary>Installe la série reçue du serveur ; sans effet si le poste l'a déjà (son compteur local fait foi).</summary>
    Task SaveDeviceSeriesAsync(DeviceSeries series, CancellationToken cancellationToken);
}

/// <summary>Réponse gardée par le serveur pour une clé d'idempotence.</summary>
/// <param name="IdempotencyKey">Clé de l'intention.</param>
/// <param name="DeviceId">Poste qui l'a envoyée.</param>
/// <param name="RequestHash">Empreinte du contenu de l'intention : la même clé avec un autre contenu est refusée.</param>
/// <param name="Result">Réponse rendue, rendue à l'identique à chaque renvoi.</param>
/// <param name="ReceivedAt">Première réception.</param>
public sealed record SyncReceipt(Guid IdempotencyKey, Guid DeviceId, string RequestHash, SyncIntentResult Result, DateTimeOffset ReceivedAt);

/// <summary>Réponses déjà données, dans la base du dossier.</summary>
public interface ISyncReceipts
{
    Task<SyncReceipt?> FindAsync(Guid idempotencyKey, CancellationToken cancellationToken);

    Task SaveAsync(SyncReceipt receipt, CancellationToken cancellationToken);
}

/// <summary>Parties du dossier que l'utilisateur peut recevoir.</summary>
public sealed record SyncFeedScope(bool Parties, bool Catalog);

/// <summary>
/// Changements du dossier courant depuis un jeton, lus dans le journal de synchronisation tenu par le serveur à chaque
/// enregistrement ; sans jeton, instantané complet par pages.
/// </summary>
public interface ISyncChangeFeed
{
    Task<Result<SyncChangesResponse>> ReadAsync(string? token, int limit, SyncFeedScope scope, CancellationToken cancellationToken);
}

public static class SyncErrors
{
    public static Error IdempotencyKeyReused => Error.Validation(Contracts.Common.ErrorCodes.Sync.IdempotencyKeyReused, "IdempotencyKey");

    public static Error TokenInvalid => Error.Validation(Contracts.Common.ErrorCodes.Sync.TokenInvalid, "Token");

    public static Error BatchInvalid => Error.Validation(Contracts.Common.ErrorCodes.Sync.BatchInvalid, "Intents");

    public static Error EntityNotFound => new(Contracts.Common.ErrorCodes.Sync.EntityNotFound, ErrorKind.NotFound);

    public static Error DeviceSeriesUnknown => new(Contracts.Common.ErrorCodes.Sync.DeviceSeriesUnknown, ErrorKind.Forbidden);

    public static Error NumberOutOfOrder => new(Contracts.Common.ErrorCodes.Sync.NumberOutOfOrder, ErrorKind.Conflict);
}

/// <summary>
/// Une portée neuve sur le dossier courant, pour le même utilisateur : chaque intention rejouée a son propre contexte,
/// et une écriture refusée (version périmée) ne reste pas en attente dans celui de l'intention suivante.
/// </summary>
public interface IDossierWorkScope
{
    Task<T> RunAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>
/// Série de numérotation d'un poste (ADR-0007) : <c>FAC-P01</c> pour la série <c>FAC</c>, numéros <c>FAC-P01-000001</c>.
/// Le serveur l'ouvre au premier passage en ligne du poste ; seul ce poste y numérote.
/// </summary>
/// <param name="BaseSeries">Série du dossier dont elle tient lieu sur le poste (<c>FAC</c>).</param>
/// <param name="Code">Code de la série du poste, unique dans le dossier.</param>
/// <param name="Prefix">Préfixe des numéros.</param>
/// <param name="DeviceId">Le poste qui numérote dans cette série.</param>
/// <param name="Next">Prochain numéro connu de celui qui répond.</param>
public sealed record DeviceSeries(string BaseSeries, string Code, string Prefix, Guid DeviceId, long Next);

/// <summary>
/// La série à utiliser pour valider sur ce poste. <c>null</c> sur le serveur, et sur un poste tant qu'il n'a pas reçu
/// de série : il ne valide alors pas hors ligne.
/// </summary>
public interface IDeviceNumbering
{
    Task<DeviceSeries?> FindAsync(string baseSeries, CancellationToken cancellationToken);
}

/// <summary>Par défaut (serveur, navigateur) : aucune série de poste, la validation numérote dans la série du dossier.</summary>
internal sealed class NoDeviceNumbering : IDeviceNumbering
{
    public Task<DeviceSeries?> FindAsync(string baseSeries, CancellationToken cancellationToken) => Task.FromResult<DeviceSeries?>(null);
}

/// <summary>Les séries de poste du dossier, côté serveur.</summary>
public interface IDeviceSeriesRegistry
{
    /// <summary>La série ouverte du poste pour <paramref name="baseSeries"/>, créée au premier appel (rejouable).</summary>
    Task<DeviceSeries> OpenAsync(Guid deviceId, string baseSeries, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Vérifie que <paramref name="number"/> est exactement le suivant de la série du poste, l'y consomme puis exécute
    /// <paramref name="work"/>, le tout dans une seule transaction : un numéro manquant, en double ou d'un autre poste
    /// est refusé, et rien n'est enregistré si <paramref name="work"/> échoue.
    /// </summary>
    Task<Result<T>> ExecuteWithDeviceNumberAsync<T>(
        string seriesCode,
        Guid deviceId,
        string number,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken);
}

/// <summary>Sans persistance de synchronisation (tests de l'application) : aucune série de poste n'existe.</summary>
internal sealed class NoDeviceSeriesRegistry : IDeviceSeriesRegistry
{
    public Task<DeviceSeries> OpenAsync(Guid deviceId, string baseSeries, DateTimeOffset now, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Aucun registre de séries de poste n'est enregistré.");

    public Task<Result<T>> ExecuteWithDeviceNumberAsync<T>(
        string seriesCode, Guid deviceId, string number, Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken) =>
        Task.FromResult<Result<T>>(SyncErrors.DeviceSeriesUnknown);
}
