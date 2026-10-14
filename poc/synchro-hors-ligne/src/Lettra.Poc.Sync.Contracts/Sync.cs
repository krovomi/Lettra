using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettra.Poc.Sync.Contracts;

// ---------------------------------------------------------------------------
// Faits : documents validés hors ligne sur un poste MAUI. Envoyés tels quels,
// revérifiés par le serveur, jamais rejoués, jamais en conflit.
// ---------------------------------------------------------------------------

/// <summary>Facture validée sur un poste, numérotée dans la série de ce poste (ADR-0007).</summary>
public sealed record ValidatedInvoiceFact(
    Guid IdempotencyKey,
    string SeriesCode,
    long SequenceNumber,
    string? PreviousHash,
    InvoiceDto Invoice,
    int? DraftBaseVersion);

public enum FactOutcome { Accepted, AlreadyKnown, Rejected }

public sealed record FactResult(Guid IdempotencyKey, Guid InvoiceId, FactOutcome Outcome, string? Detail);

public sealed record PushFactsRequest(Guid DeviceId, IReadOnlyList<ValidatedInvoiceFact> Facts);
public sealed record PushFactsResponse(IReadOnlyList<FactResult> Results);

// ---------------------------------------------------------------------------
// Intentions : commandes Contracts rejouées par le serveur avec ses règles.
// Idempotency-Key pour les reprises, version de départ (ETag) pour les conflits.
// ---------------------------------------------------------------------------

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreateDraftCommand), "create-draft")]
[JsonDerivedType(typeof(UpdateDraftCommand), "update-draft")]
[JsonDerivedType(typeof(ValidateDraftCommand), "validate-draft")]
public abstract record DraftCommand(Guid IdempotencyKey, Guid InvoiceId);

public sealed record CreateDraftCommand(Guid IdempotencyKey, InvoiceDto Draft)
    : DraftCommand(IdempotencyKey, Draft.Id);

/// <summary>
/// Modification d'un brouillon. Chaque champ modifié porte sa valeur de départ (Base)
/// et la valeur voulue (Mine) : c'est ce qui permet la fusion champ par champ.
/// </summary>
public sealed record UpdateDraftCommand(
    Guid IdempotencyKey,
    Guid InvoiceId,
    int BaseVersion,
    IReadOnlyDictionary<string, FieldChange> Changes)
    : DraftCommand(IdempotencyKey, InvoiceId);

/// <summary>Validation différée (navigateur) : le serveur numérote à la synchronisation.</summary>
public sealed record ValidateDraftCommand(Guid IdempotencyKey, Guid InvoiceId, string ExpectedContent)
    : DraftCommand(IdempotencyKey, InvoiceId);

/// <summary>Valeurs sérialisées en JSON pour rester génériques (string, date, liste de lignes).</summary>
public sealed record FieldChange(JsonElement Base, JsonElement Mine);

public enum IntentOutcome { Applied, Merged, Conflict, Ignored, Rejected }

public sealed record FieldConflict(string Field, JsonElement Base, JsonElement Mine, JsonElement Server);

public sealed record IntentResult(
    Guid IdempotencyKey,
    Guid InvoiceId,
    IntentOutcome Outcome,
    int? NewVersion,
    IReadOnlyList<string> AppliedFields,
    IReadOnlyList<FieldConflict> Conflicts,
    string? Detail);

public sealed record PushIntentsRequest(Guid DeviceId, IReadOnlyList<DraftCommand> Commands);
public sealed record PushIntentsResponse(IReadOnlyList<IntentResult> Results);

// ---------------------------------------------------------------------------
// Réception : changements serveur depuis un jeton de synchro.
// ---------------------------------------------------------------------------

public sealed record ChangesResponse(long SyncToken, IReadOnlyList<InvoiceDto> Invoices);

public sealed record HealthResponse(string Service, DateTimeOffset ServerTime);

public static class SyncHttp
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ServiceName = "lettra-sync";
    public const string DeviceRevokedProblem = "https://lettra.fr/problems/device-revoked";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

// ---------------------------------------------------------------------------
// Authentification du poste (simplifiée : en vrai, OIDC Keycloak, ADR-0010).
// ---------------------------------------------------------------------------

public sealed record LoginRequest(Guid DeviceId, string Secret);
public sealed record RefreshRequest(Guid DeviceId, string RefreshToken);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);
