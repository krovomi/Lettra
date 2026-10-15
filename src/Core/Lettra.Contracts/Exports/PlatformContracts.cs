using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Exports;

/// <summary>
/// Statut du cycle de vie d'une facture électronique à la plateforme agréée (norme AFNOR XP Z12-012). La valeur est le
/// code du statut. Déposée, Rejetée, Refusée et Encaissée sont les quatre statuts obligatoires.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformStatusDto>))]
public enum PlatformStatusDto
{
    /// <summary>Déposée : la plateforme du vendeur a reçu la facture.</summary>
    Deposited = 200,

    /// <summary>Émise par la plateforme du vendeur.</summary>
    Issued = 201,

    /// <summary>Reçue par la plateforme de l'acheteur.</summary>
    Received = 202,

    /// <summary>Mise à disposition de l'acheteur.</summary>
    MadeAvailable = 203,

    /// <summary>Prise en charge par l'acheteur.</summary>
    InProgress = 204,

    /// <summary>Approuvée par l'acheteur.</summary>
    Approved = 205,

    /// <summary>Approuvée partiellement.</summary>
    PartiallyApproved = 206,

    /// <summary>En litige.</summary>
    Disputed = 207,

    /// <summary>Suspendue : l'acheteur attend un complément.</summary>
    Suspended = 208,

    /// <summary>Complétée : le vendeur a apporté le complément.</summary>
    Completed = 209,

    /// <summary>Refusée par l'acheteur (définitif) : la pièce se corrige par un avoir.</summary>
    Refused = 210,

    /// <summary>Paiement transmis par l'acheteur.</summary>
    PaymentSent = 211,

    /// <summary>Encaissée : le vendeur déclare l'encaissement (définitif).</summary>
    Collected = 212,

    /// <summary>Rejetée par la plateforme (format, contrôles, doublon) : définitif, la pièce peut être retransmise.</summary>
    Rejected = 213,
}

/// <summary>Voie de transmission d'une pièce à la plateforme agréée.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlatformChannelDto>))]
public enum PlatformChannelDto
{
    /// <summary>Facture électronique : client professionnel établi en France (SIREN).</summary>
    EInvoice = 1,

    /// <summary>E-reporting de la transaction : client particulier ou établi hors de France.</summary>
    EReporting = 2,
}

/// <summary>Transmission d'une pièce validée à la plateforme agréée.</summary>
public sealed record TransmitInvoiceRequest
{
    /// <summary>Syntaxe de la facture électronique ; CII par défaut (le XML d'un Factur-X).</summary>
    public EInvoiceFormatDto Format { get; set; } = EInvoiceFormatDto.Cii;
}

/// <summary>Encaissement d'une pièce, déclaré à la plateforme (statut Encaissée ou e-reporting du paiement).</summary>
public sealed record ReportPaymentRequest
{
    public DateOnly Date { get; set; }

    [Range(0.01d, 999_999_999.9999d, ErrorMessage = ErrorCodes.Platform.PaymentAmountInvalid)]
    public decimal Amount { get; set; }
}

/// <summary>
/// Un statut reçu de la plateforme (ou déclaré par le vendeur), avec son motif éventuel ; <paramref name="Amount"/> est
/// le montant encaissé d'un statut Encaissée.
/// </summary>
public sealed record PlatformStatusEventResponse(PlatformStatusDto Status, DateTimeOffset At, string? Reason, decimal? Amount);

/// <summary>
/// La dernière transmission d'une pièce : voie, plateforme, référence attribuée par la plateforme, statut courant et
/// historique des statuts dans l'ordre. <paramref name="IsFinal"/> : Refusée, Rejetée, ou Encaissée pour tout le TTC
/// (<paramref name="CollectedAmount"/> cumule les encaissements déclarés).
/// </summary>
public sealed record PlatformTransmissionResponse(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    PlatformChannelDto Channel,
    EInvoiceFormatDto? Format,
    string Platform,
    string? PlatformReference,
    PlatformStatusDto Status,
    bool IsFinal,
    decimal CollectedAmount,
    DateTimeOffset SubmittedAt,
    IReadOnlyList<PlatformStatusEventResponse> History);
