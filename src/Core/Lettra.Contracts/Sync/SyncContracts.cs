using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Suppliers;

namespace Lettra.Contracts.Sync;

/// <summary>
/// Intention : une commande saisie sur un poste hors ligne, gardée dans son outbox puis rejouée par le serveur avec
/// ses propres règles et les droits du moment (ADR-0008). Le format est celui des contrats : pas de troisième modèle.
/// <para>
/// <see cref="IdempotencyKey"/> identifie l'intention pour toujours : un renvoi après une réponse perdue rend le même
/// résultat sans rien rejouer. Une intention n'est jamais réécrite dans l'outbox (constat 4 du POC).
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreateCustomerIntent), "customer.create")]
[JsonDerivedType(typeof(UpdateCustomerIntent), "customer.update")]
[JsonDerivedType(typeof(CreateSupplierIntent), "supplier.create")]
[JsonDerivedType(typeof(UpdateSupplierIntent), "supplier.update")]
[JsonDerivedType(typeof(CreateCatalogItemIntent), "catalog-item.create")]
[JsonDerivedType(typeof(UpdateCatalogItemIntent), "catalog-item.update")]
[JsonDerivedType(typeof(SetCatalogItemArchivedIntent), "catalog-item.archive")]
[JsonDerivedType(typeof(ValidatedInvoiceFact), "invoice.validated")]
public abstract record SyncIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt);

/// <summary>Éléments synchronisés. Phase 4.1 : les tiers et le catalogue ; 4.2 : les factures validées hors ligne.</summary>
public enum SyncEntityKind
{
    Customer = 1,
    Supplier = 2,
    CatalogItem = 3,
    Invoice = 4,
}

/// <summary>Création d'un client avec l'identifiant choisi sur le poste : le serveur crée le même.</summary>
public sealed record CreateCustomerIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, CreateCustomerRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

/// <summary>
/// Modification d'un client. <see cref="Base"/> est l'état lu sur le poste juste avant la saisie : le serveur compare
/// le contenu, pas la version, pour fusionner champ par champ (constat 2 du POC).
/// </summary>
public sealed record UpdateCustomerIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, CreateCustomerRequest Base, CreateCustomerRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

public sealed record CreateSupplierIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, CreateSupplierRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

public sealed record UpdateSupplierIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, CreateSupplierRequest Base, CreateSupplierRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

public sealed record CreateCatalogItemIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, SaveCatalogItemRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

public sealed record UpdateCatalogItemIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, SaveCatalogItemRequest Base, SaveCatalogItemRequest Data)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

/// <summary>Archivage ou réactivation d'un article ; <see cref="WasArchived"/> est l'état vu sur le poste.</summary>
public sealed record SetCatalogItemArchivedIntent(Guid IdempotencyKey, Guid EntityId, DateTimeOffset RecordedAt, bool WasArchived, bool IsArchived)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

/// <summary>Ce que le serveur a fait d'une intention.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SyncIntentOutcome>))]
public enum SyncIntentOutcome
{
    /// <summary>Appliquée telle quelle : personne n'avait touché ces champs entre-temps.</summary>
    Applied = 1,

    /// <summary>Appliquée par-dessus des changements faits au bureau sur d'autres champs.</summary>
    Merged = 2,

    /// <summary>Les champs sans conflit sont appliqués ; les champs disputés gardent la valeur du serveur et attendent l'utilisateur.</summary>
    Conflict = 3,

    /// <summary>Refusée par les règles ou les droits du serveur : rien n'est appliqué (voir <see cref="SyncIntentResult.Errors"/>).</summary>
    Rejected = 4,

    /// <summary>Déjà appliquée (renvoi après une réponse perdue, ou même valeur déjà présente).</summary>
    AlreadyApplied = 5,
}

/// <summary>Un champ modifié des deux côtés avec des valeurs différentes. Les valeurs sont celles du contrat, en JSON.</summary>
public sealed record SyncFieldConflict(string Field, JsonElement Base, JsonElement Mine, JsonElement Server);

public sealed record SyncIntentResult(
    Guid IdempotencyKey,
    Guid EntityId,
    SyncIntentOutcome Outcome,
    long? ServerVersion,
    IReadOnlyList<SyncFieldConflict> Conflicts,
    IReadOnlyList<ErrorDetail> Errors);

/// <summary>Envoi d'un lot d'intentions, dans l'ordre de leur saisie.</summary>
public sealed record PushSyncIntentsRequest(Guid DeviceId, IReadOnlyList<SyncIntent> Intents);

public sealed record PushSyncIntentsResponse(IReadOnlyList<SyncIntentResult> Results);

/// <summary>
/// Changements du serveur depuis un jeton opaque (absent : instantané complet, par pages). Un élément n'y figure
/// qu'une fois, dans son état actuel. Le poste garde <see cref="Token"/> et redemande tant que <see cref="HasMore"/>.
/// Les éléments que l'utilisateur n'a pas le droit de lire sont absents (tiers ou catalogue). La dernière page porte
/// la fiche entreprise du dossier, dont le poste a besoin pour valider une facture hors ligne (une ligne, toujours à jour).
/// </summary>
public sealed record SyncChangesResponse(
    string Token,
    bool HasMore,
    IReadOnlyList<CustomerResponse> Customers,
    IReadOnlyList<SupplierResponse> Suppliers,
    IReadOnlyList<CatalogItemResponse> CatalogItems,
    CompanyProfileResponse? CompanyProfile = null,
    SyncPolicyResponse? Policy = null,
    IReadOnlyList<string>? Released = null)
{
    [JsonIgnore]
    public int Count => Customers.Count + Suppliers.Count + CatalogItems.Count;
}

public static class SyncIntents
{
    /// <summary>
    /// Refus d'attente : une pièce précédente du même poste manque ou est refusée. La pièce n'est ni reçue ni refusée
    /// pour de bon : elle repartira avec la même clé une fois la précédente reçue (étape 4.4).
    /// </summary>
    public static bool IsWaiting(this SyncIntentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome == SyncIntentOutcome.Rejected && result.Errors.Count > 0
            && result.Errors.All(e => e.Code == Common.ErrorCodes.Sync.NumberOutOfOrder);
    }

    /// <summary>Nature de l'élément visé, pour regrouper les intentions et les changements reçus.</summary>
    public static SyncEntityKind EntityOf(this SyncIntent intent) => intent switch
    {
        CreateCustomerIntent or UpdateCustomerIntent => SyncEntityKind.Customer,
        CreateSupplierIntent or UpdateSupplierIntent => SyncEntityKind.Supplier,
        CreateCatalogItemIntent or UpdateCatalogItemIntent or SetCatalogItemArchivedIntent => SyncEntityKind.CatalogItem,
        ValidatedInvoiceFact => SyncEntityKind.Invoice,
        _ => throw new ArgumentOutOfRangeException(nameof(intent), intent?.GetType().Name, "Intention inconnue."),
    };
}

/// <summary>
/// Fait : une facture, un acompte ou un avoir validé hors ligne, numéroté dans la série du poste (ADR-0007). Il part tel
/// qu'il a été remis au client ; le serveur le revérifie (suite des numéros, totaux, mentions, empreinte) et ne le
/// fusionne jamais : un fait n'est pas en conflit (ADR-0008). Les saisies du brouillon ne partent pas, le fait les couvre.
/// </summary>
/// <param name="IdempotencyKey">Clé du fait, tirée à la validation sur le poste.</param>
/// <param name="EntityId">Identifiant de la pièce.</param>
/// <param name="RecordedAt">Moment de la validation sur le poste.</param>
/// <param name="Document">La pièce figée, avec sa série, son numéro et son empreinte.</param>
/// <param name="DownPaymentPositions">Lignes comptées en acompte reçu (4191) : la nature ne se déduit pas du reste.</param>
/// <param name="CustomerIsBusiness">Le client était une entreprise (mentions B2B) : la copie du client ne le dit pas.</param>
/// <param name="LatePenaltyRate">Taux des pénalités de retard figé à la validation (absent de la réponse de lecture).</param>
/// <param name="EarlyPaymentDiscount">Escompte figé à la validation (absent de la réponse de lecture).</param>
public sealed record ValidatedInvoiceFact(
    Guid IdempotencyKey,
    Guid EntityId,
    DateTimeOffset RecordedAt,
    InvoiceResponse Document,
    IReadOnlyList<int> DownPaymentPositions,
    bool CustomerIsBusiness,
    decimal? LatePenaltyRate,
    string? EarlyPaymentDiscount)
    : SyncIntent(IdempotencyKey, EntityId, RecordedAt);

/// <summary>Demande des séries de numérotation d'un poste : une par série du dossier qui se valide hors ligne.</summary>
/// <param name="DeviceId">Identifiant du poste, tiré au premier lancement (le même que celui de l'instantané de droits).</param>
public sealed record OpenDeviceSeriesRequest(Guid DeviceId);

/// <summary>Une série de poste : <c>FAC-P01</c> tient lieu de <c>FAC</c> sur ce poste, numéros <c>FAC-P01-000001</c>.</summary>
/// <param name="BaseSeries">Série du dossier remplacée sur le poste.</param>
/// <param name="SeriesCode">Code de la série du poste.</param>
/// <param name="Prefix">Préfixe des numéros.</param>
/// <param name="Next">Prochain numéro attendu par le serveur.</param>
public sealed record DeviceSeriesResponse(string BaseSeries, string SeriesCode, string Prefix, long Next);

/// <summary>Les séries ouvertes pour le poste.</summary>
public sealed record OpenDeviceSeriesResponse(IReadOnlyList<DeviceSeriesResponse> Series);

/// <summary>
/// Règle du cabinet pour les postes du dossier (étape 4.4), envoyée avec la dernière page reçue : au-delà de
/// <paramref name="MaxOfflineHours"/> sans contact avec le serveur, le poste ne valide plus de pièce.
/// </summary>
/// <param name="MaxOfflineHours">Durée maximale hors ligne, en heures (de 1 à 720).</param>
public sealed record SyncPolicyResponse(int MaxOfflineHours);

/// <summary>Règle des postes à enregistrer pour le dossier.</summary>
public sealed record SaveSyncPolicyRequest
{
    /// <summary>Durée maximale hors ligne, en heures (de 1 à 720, soit 30 jours).</summary>
    public int MaxOfflineHours { get; init; }
}

/// <summary>Un poste qui s'est synchronisé avec le dossier (registre du serveur, étape 4.4).</summary>
/// <param name="DeviceId">Identifiant du poste, tiré à son premier lancement.</param>
/// <param name="FirstSeenAt">Premier contact avec le serveur.</param>
/// <param name="LastSeenAt">Dernier contact.</param>
/// <param name="LastUserId">Utilisateur du dernier contact.</param>
/// <param name="RevokedAt">Révocation : le poste est refusé et sa réplique effacée à son prochain contact.</param>
/// <param name="RevokedBy">Qui l'a révoqué.</param>
/// <param name="WipedAt">Le poste a reçu sa révocation (sa réplique est effacée).</param>
public sealed record SyncDeviceResponse(
    Guid DeviceId,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    Guid? LastUserId,
    DateTimeOffset? RevokedAt,
    Guid? RevokedBy,
    DateTimeOffset? WipedAt);

/// <summary>
/// Déblocage d'une série de poste (étape 4.4) : le cabinet accepte, sous l'autorité de qui débloque, la pièce attendue
/// sous ce numéro, refusée à sa réception (approbation manquante, droit retiré, règle changée). Elle a été remise au
/// client : elle entre dans la chaîne, et se corrige s'il le faut par un avoir.
/// </summary>
/// <param name="SeriesCode">Série du poste.</param>
/// <param name="Number">Numéro accepté.</param>
public sealed record ReleaseDeviceSeriesResponse(string SeriesCode, string Number);

public static class SyncHttp
{
    /// <summary>En-tête qui porte l'identifiant du poste sur chaque appel de synchronisation.</summary>
    public const string DeviceHeader = "X-Lettra-Device";

    /// <summary>Registre des postes du dossier ; <c>/{deviceId}/revocation</c> révoque un poste.</summary>
    public const string DevicesPath = "/api/v1/sync/devices";

    /// <summary>Règle des postes du dossier (durée maximale hors ligne).</summary>
    public const string PolicyPath = "/api/v1/sync/policy";

    /// <summary>Route d'envoi des intentions.</summary>
    public const string IntentsPath = "/api/v1/sync/intents";

    /// <summary>Route de réception des changements (<c>?token=…&amp;limit=…</c>).</summary>
    public const string ChangesPath = "/api/v1/sync/changes";

    /// <summary>Route d'ouverture des séries de numérotation du poste (ADR-0007).</summary>
    public const string DeviceSeriesPath = "/api/v1/sync/device-series";

    /// <summary>Taille maximale d'un lot d'intentions accepté par le serveur.</summary>
    public const int MaxBatch = 200;
}
