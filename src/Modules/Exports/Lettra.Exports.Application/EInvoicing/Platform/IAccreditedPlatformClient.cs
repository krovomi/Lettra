using Lettra.Contracts.Exports;
using Lettra.Domain.Common;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// Une facture électronique déposée à la plateforme du vendeur. <c>IdempotencyKey</c> est la clé de l'envoi (outbox,
/// étape 5.4) : la plateforme rend l'accusé du premier dépôt fait sous cette clé au lieu de traiter un doublon. Même rôle
/// sur les autres appels.
/// </summary>
public sealed record PlatformInvoiceSubmission(
    string SellerSiren,
    string BuyerSiren,
    string InvoiceNumber,
    EInvoiceFormatDto Format,
    string FileName,
    ReadOnlyMemory<byte> Content,
    string? IdempotencyKey = null);

/// <summary>Nature d'une transaction déclarée en e-reporting.</summary>
public enum PlatformTransactionKind
{
    /// <summary>Vente à un particulier (B2C).</summary>
    Consumer = 1,

    /// <summary>Vente à un professionnel établi hors de France (B2B international).</summary>
    International = 2,
}

/// <summary>Une ventilation de TVA déclarée (catégorie EN 16931, taux, base, montant).</summary>
public sealed record PlatformVatAmount(string Category, decimal Rate, decimal Basis, decimal Amount);

/// <summary>
/// Une pièce déclarée en e-reporting de transaction, pièce par pièce. La plateforme agrège selon la périodicité du régime
/// de TVA du vendeur.
/// </summary>
public sealed record PlatformTransactionReport(
    string SellerSiren,
    string InvoiceNumber,
    DateOnly Date,
    bool IsCreditNote,
    PlatformTransactionKind Kind,
    string BuyerCountryCode,
    string Currency,
    IReadOnlyList<PlatformVatAmount> Vat,
    decimal TotalExclTax,
    decimal TotalVat,
    decimal TotalInclTax,
    string? IdempotencyKey = null);

/// <summary>Un encaissement : statut Encaissée d'une facture électronique, ou e-reporting du paiement d'une transaction.</summary>
public sealed record PlatformPaymentReport(
    string SellerSiren,
    string InvoiceNumber,
    string Reference,
    PlatformChannelDto Channel,
    DateOnly Date,
    decimal Amount,
    string? IdempotencyKey = null);

/// <summary>Un statut du cycle de vie, tel que la plateforme le restitue.</summary>
public sealed record PlatformStatusEvent(PlatformStatusDto Status, DateTimeOffset At, string? Reason = null, decimal? Amount = null);

/// <summary>Accusé de dépôt : la référence attribuée par la plateforme et le premier statut (Déposée ou Rejetée).</summary>
public sealed record PlatformReceipt(string Reference, PlatformStatusEvent Status);

/// <summary>
/// Le port vers la plateforme agréée partenaire (annexe A, <c>IPlateformeAgreeeClient</c>) : dépôt des factures
/// électroniques, e-reporting des transactions et des paiements, lecture des statuts du cycle de vie. Les adapters (PA
/// simulée, puis la vraie PA en 5.5) passent les mêmes tests de contrat. Appelé côté serveur seulement : le poste hors
/// ligne transmet par la synchronisation, jamais directement.
/// </summary>
/// <remarks>
/// Échecs attendus : <c>Platform.NotConfigured</c> (aucune plateforme), <c>Platform.Unavailable</c> (plateforme
/// injoignable : rien n'a été transmis), <c>Platform.ReferenceUnknown</c>. Un refus de contenu n'est pas un échec : c'est
/// un accusé au statut Rejetée, avec son motif.
/// </remarks>
public interface IAccreditedPlatformClient
{
    /// <summary>Nom affiché de la plateforme, enregistré avec chaque transmission.</summary>
    string Name { get; }

    Task<Result<PlatformReceipt>> SubmitInvoiceAsync(PlatformInvoiceSubmission submission, CancellationToken cancellationToken);

    Task<Result<PlatformReceipt>> ReportTransactionAsync(PlatformTransactionReport report, CancellationToken cancellationToken);

    /// <summary>Tout l'historique connu de la plateforme pour cette référence, dans l'ordre.</summary>
    Task<Result<IReadOnlyList<PlatformStatusEvent>>> GetStatusEventsAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Déclare un encaissement ; la plateforme l'ajoute à l'historique (statut Encaissée).</summary>
    Task<Result<PlatformStatusEvent>> ReportPaymentAsync(PlatformPaymentReport payment, CancellationToken cancellationToken);
}

public static class PlatformErrors
{
    public static Error NotConfigured => Error.Conflict(Contracts.Common.ErrorCodes.Platform.NotConfigured);

    public static Error Unavailable => new(Contracts.Common.ErrorCodes.Platform.Unavailable, ErrorKind.Unexpected);

    public static Error SellerSirenMissing => Error.Conflict(Contracts.Common.ErrorCodes.Platform.SellerSirenMissing);

    public static Error NotTransmitted => Error.NotFound(Contracts.Common.ErrorCodes.Platform.NotTransmitted);

    public static Error AlreadyFinal => Error.Conflict(Contracts.Common.ErrorCodes.Platform.AlreadyFinal);

    public static Error PaymentAmountInvalid => Error.Validation(Contracts.Common.ErrorCodes.Platform.PaymentAmountInvalid, "Amount");

    public static Error PaymentDateInvalid => Error.Validation(Contracts.Common.ErrorCodes.Platform.PaymentDateInvalid, "Date");

    public static Error ReferenceUnknown => Error.NotFound(Contracts.Common.ErrorCodes.Platform.ReferenceUnknown);

    /// <summary>L'envoi est en file : le Worker l'enverra (envoi immédiat désactivé, ou repris après un incident).</summary>
    public static Error Queued => Error.Conflict(Contracts.Common.ErrorCodes.Platform.Queued);
}

/// <summary>Aucune plateforme configurée (par défaut) : tout échoue, rien ne part.</summary>
internal sealed class UnconfiguredPlatformClient : IAccreditedPlatformClient
{
    public string Name => "Aucune";

    public Task<Result<PlatformReceipt>> SubmitInvoiceAsync(PlatformInvoiceSubmission submission, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PlatformReceipt>>(PlatformErrors.NotConfigured);

    public Task<Result<PlatformReceipt>> ReportTransactionAsync(PlatformTransactionReport report, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PlatformReceipt>>(PlatformErrors.NotConfigured);

    public Task<Result<IReadOnlyList<PlatformStatusEvent>>> GetStatusEventsAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<PlatformStatusEvent>>>(PlatformErrors.NotConfigured);

    public Task<Result<PlatformStatusEvent>> ReportPaymentAsync(PlatformPaymentReport payment, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PlatformStatusEvent>>(PlatformErrors.NotConfigured);
}
