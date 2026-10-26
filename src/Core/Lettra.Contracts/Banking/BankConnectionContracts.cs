using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Banking;

/// <summary>État d'une banque connectée : consentement demandé, actif, échu (à renouveler) ou déconnectée.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankConnectionStatusDto>))]
public enum BankConnectionStatusDto
{
    Pending = 1,
    Active = 2,
    Revoked = 3,
    Expired = 4,
}

/// <summary>Une banque que l'agrégateur sait relier.</summary>
public sealed record BankInstitutionResponse(string Id, string Name);

/// <summary>Un compte relié : banque, compte masqué, échéance du consentement, dernière synchronisation.</summary>
public sealed record BankConnectionResponse(
    Guid Id,
    string InstitutionId,
    string InstitutionName,
    string? AccountDisplay,
    BankConnectionStatusDto Status,
    DateTimeOffset? ConsentExpiresAt,
    bool NeedsRenewal,
    DateTimeOffset? LastSyncAt,
    int LastSyncImported,
    DateTimeOffset CreatedAt);

/// <summary>
/// Les banques connectées du dossier. <see cref="Available"/> est faux quand aucun agrégateur n'est configuré : l'écran
/// propose alors seulement l'import de fichiers.
/// </summary>
public sealed record BankConnectionsResponse(
    bool Available,
    string? Provider,
    IReadOnlyList<BankInstitutionResponse> Institutions,
    IReadOnlyList<BankConnectionResponse> Connections);

/// <summary>Relier un compte d'une banque ; avec <see cref="ReplacesId"/>, renouveler le consentement d'un compte déjà relié.</summary>
public sealed record StartBankConnectionRequest
{
    [Required(ErrorMessage = ErrorCodes.BankConnection.InstitutionUnknown)]
    [StringLength(60, ErrorMessage = ErrorCodes.BankConnection.InstitutionUnknown)]
    public string InstitutionId { get; set; } = string.Empty;

    public Guid? ReplacesId { get; set; }
}

/// <summary>La page de la banque où donner le consentement, puis revenir sur Lettra.</summary>
public sealed record BankConsentRedirectResponse(Guid ConnectionId, string RedirectUrl);

/// <summary>Le retour de la banque : le code qu'elle a joint à l'adresse de retour (accord ou refus).</summary>
public sealed record CompleteBankConnectionRequest
{
    [StringLength(200, ErrorMessage = ErrorCodes.BankConnection.ConsentRefused)]
    public string? Code { get; set; }
}

/// <summary>Synchroniser un compte relié, ou tous ; <see cref="OnlyDue"/> : seulement ceux relus il y a plus de 6 heures.</summary>
public sealed record SyncBankConnectionsRequest
{
    public Guid? ConnectionId { get; set; }

    public bool OnlyDue { get; set; }
}

/// <summary>Compte rendu de la synchronisation d'un compte : opérations nouvelles et propositions, ou ses refus.</summary>
public sealed record BankConnectionSyncOutcome(
    Guid ConnectionId,
    string InstitutionName,
    int Imported,
    int Duplicates,
    int Proposals,
    IReadOnlyList<ErrorDetail> Errors);

public sealed record SyncBankConnectionsResponse(IReadOnlyList<BankConnectionSyncOutcome> Outcomes);

/// <summary>
/// Le retour de la banque. Accord (<see cref="Granted"/>) : le compte relié et sa première synchronisation (90 jours
/// d'historique). Refus sur la page de la banque : une issue normale du parcours, pas une erreur ; la connexion reste
/// en attente, sans opération lue.
/// </summary>
public sealed record BankConnectionCompletedResponse(bool Granted, BankConnectionResponse Connection, BankConnectionSyncOutcome FirstSync);

/// <summary>
/// Les pages de l'application dans le parcours de consentement : le retour de la banque (<see cref="Callback"/>, avec
/// <c>connection</c> et <c>code</c>), et la page qui joue la banque pour l'agrégateur simulé.
/// </summary>
public static class BankConnectionRoutes
{
    public const string Callback = "bank/connected";

    public const string SimulatedConsent = "bank/simulated-consent";

    /// <summary>Le code de retour de la banque simulée quand le consentement est donné.</summary>
    public const string SimulatedGrantedCode = "accorde";

    /// <summary>Le code de retour de la banque simulée quand il est refusé.</summary>
    public const string SimulatedRefusedCode = "refuse";
}
