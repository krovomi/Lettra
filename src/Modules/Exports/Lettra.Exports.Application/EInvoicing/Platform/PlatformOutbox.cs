using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>Dépôt d'une pièce à la plateforme agréée, mis en file. <paramref name="Previous"/> : la transmission rejetée qu'il remplace.</summary>
internal sealed record PlatformDepositOrder(Guid InvoiceId, EInvoiceFormatDto Format, Guid? Previous);

/// <summary>Encaissement déclaré, mis en file. <paramref name="KnownCount"/> : statuts connus de la transmission à la déclaration.</summary>
internal sealed record PlatformPaymentOrder(Guid InvoiceId, Guid TransmissionId, DateOnly Date, decimal Amount, int KnownCount);

[JsonSerializable(typeof(PlatformDepositOrder))]
[JsonSerializable(typeof(PlatformPaymentOrder))]
internal sealed partial class PlatformOutboxJson : JsonSerializerContext;

/// <summary>
/// Les envois à la plateforme agréée passent par l'outbox du serveur (étape 5.4) : la commande écrit l'ordre dans la base
/// du dossier, le répartiteur l'envoie aussitôt (chemin synchrone) ou le reprend plus tard (Worker). La clé
/// d'idempotence part avec l'appel : un envoi repris après un arrêt rend l'accusé du premier, sans doublon.
/// </summary>
internal static class PlatformOutbox
{
    public const string DepositKind = "pa.depot";
    public const string PaymentKind = "pa.encaissement";

    /// <summary>Un dépôt par pièce, ou un nouveau par transmission rejetée qu'il remplace.</summary>
    public static string DepositKey(Guid invoiceId, Guid? previous) =>
        string.Create(CultureInfo.InvariantCulture, $"pa-depot:{invoiceId:N}:{previous?.ToString("N") ?? "0"}");

    /// <summary>La même déclaration (mêmes date et montant sur le même état) ne part qu'une fois, même cliquée deux fois.</summary>
    public static string PaymentKey(Guid transmissionId, int knownCount, DateOnly date, decimal amount) =>
        string.Create(CultureInfo.InvariantCulture, $"pa-encaissement:{transmissionId:N}:{knownCount}:{date:yyyyMMdd}:{amount}");

    public static string Write(PlatformDepositOrder order) => JsonSerializer.Serialize(order, PlatformOutboxJson.Default.PlatformDepositOrder);

    public static string Write(PlatformPaymentOrder order) => JsonSerializer.Serialize(order, PlatformOutboxJson.Default.PlatformPaymentOrder);

    public static PlatformDepositOrder? ReadDeposit(string payload) => Read(payload, PlatformOutboxJson.Default.PlatformDepositOrder);

    public static PlatformPaymentOrder? ReadPayment(string payload) => Read(payload, PlatformOutboxJson.Default.PlatformPaymentOrder);

    private static T? Read<T>(string payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(payload, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Envoi immédiat par la requête : un ordre mis de côté, ou en attente de son prochain essai, repart tout de suite
    /// puisque l'utilisateur le redemande.
    /// </summary>
    public static async Task<OutboxOutcome?> SendNowAsync(IOutboxDispatcher dispatcher, Guid message, CancellationToken cancellationToken)
    {
        await dispatcher.RequeueAsync(message, cancellationToken).ConfigureAwait(false);
        return await dispatcher.DispatchNowAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// L'ordre n'est pas parti tout de suite : refus définitif (son motif), sinon en file (plateforme injoignable, envoi
    /// laissé au Worker) : il repartira tout seul.
    /// </summary>
    public static Result<PlatformTransmissionResponse> NotSent(OutboxOutcome? outcome) => outcome switch
    {
        { Result: OutboxResult.Poison, Reason: string code } => FromCode(code),
        _ => PlatformErrors.Queued,
    };

    /// <summary>Un échec de la plateforme : passager (injoignable, pas encore configurée) ou définitif (le reste).</summary>
    public static OutboxOutcome Failed(IReadOnlyList<Error> errors)
    {
        string code = errors.Count > 0 ? errors[0].Code : ErrorCodes.Platform.Unavailable;
        return code is ErrorCodes.Platform.Unavailable or ErrorCodes.Platform.NotConfigured ? OutboxOutcome.Retry(code) : OutboxOutcome.Poison(code);
    }

    private static Error FromCode(string code) => code switch
    {
        ErrorCodes.Platform.NotTransmitted => PlatformErrors.NotTransmitted,
        ErrorCodes.Platform.AlreadyFinal => PlatformErrors.AlreadyFinal,
        ErrorCodes.Platform.PaymentAmountInvalid => PlatformErrors.PaymentAmountInvalid,
        ErrorCodes.Platform.ReferenceUnknown => PlatformErrors.ReferenceUnknown,
        ErrorCodes.Platform.SellerSirenMissing => PlatformErrors.SellerSirenMissing,
        ErrorCodes.EInvoice.NotValidated => EInvoiceErrors.NotValidated,
        _ => new Error(code, ErrorKind.Unexpected),
    };
}

/// <summary>
/// Envoie un dépôt : facture électronique ou e-reporting, puis enregistrement de l'accusé avec ses entrées au journal
/// inaltérable (une transaction). Un essai repris après l'enregistrement ne renvoie rien : la transmission est déjà là.
/// L'émission de la pièce reste à la commande, sous l'identité de l'utilisateur : envoyée par le Worker, la pièce
/// déposée s'émet à sa prochaine transmission demandée.
/// </summary>
internal sealed class PlatformDepositHandler(
    IInvoiceRepository invoices,
    ICompanyProfileRepository profiles,
    IPlatformTransmissionStore transmissions,
    IAccreditedPlatformClient platform,
    IBillingTransaction transaction,
    IIntegrityJournal journal,
    TimeProvider clock) : IOutboxHandler
{
    public string Kind => PlatformOutbox.DepositKind;

    public async Task<OutboxOutcome> HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (PlatformOutbox.ReadDeposit(delivery.Payload) is not { } order)
        {
            return OutboxOutcome.Poison("Ordre de dépôt illisible.");
        }

        Invoice? invoice = await invoices.FindAsync(new InvoiceId(order.InvoiceId), cancellationToken).ConfigureAwait(false);
        if (invoice is null || invoice.IsDraft || invoice.Number is null || invoice.Seller is null || string.IsNullOrWhiteSpace(invoice.Seller.Siren))
        {
            return OutboxOutcome.Poison(invoice is null ? ErrorCodes.Invoice.NotFound : ErrorCodes.EInvoice.NotValidated);
        }

        PlatformTransmission? latest = await transmissions.FindLatestAsync(invoice.Id.Value, cancellationToken).ConfigureAwait(false);
        if (latest is not null && latest.Id != order.Previous)
        {
            return OutboxOutcome.Delivered;   // déjà enregistré par un essai interrompu avant son bilan
        }

        Result<PlatformTransmission> deposited = await DepositAsync(invoice, order.Format, delivery.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (deposited.IsFailure)
        {
            return PlatformOutbox.Failed(deposited.Errors);
        }

        Result recorded = await TransmitInvoiceHandler.RecordAsync(transmissions, transaction, journal, invoice, deposited.Value, 0, cancellationToken)
            .ConfigureAwait(false);

        // Accusé non enregistré : l'essai suivant le redemande sous la même clé, la plateforme rend le même.
        return recorded.IsFailure ? OutboxOutcome.Retry(recorded.Errors[0].Code) : OutboxOutcome.Delivered;
    }

    private async Task<Result<PlatformTransmission>> DepositAsync(Invoice invoice, EInvoiceFormatDto format, string key, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        InvoiceResponse piece = invoice.ToResponse();
        bool eInvoice = TransmitInvoiceHandler.IsDomesticBusiness(invoice.Customer);
        DateOnly? preceding = eInvoice
            ? await EInvoiceSerializer.PrecedingInvoiceDateAsync(invoices, invoice, cancellationToken).ConfigureAwait(false)
            : null;
        Result<PlatformReceipt> receipt = eInvoice
            ? await platform.SubmitInvoiceAsync(
                new PlatformInvoiceSubmission(
                    invoice.Seller!.Siren!,
                    invoice.Customer.Siren!,
                    invoice.Number!,
                    format,
                    EInvoiceSerializer.FileNameOf(invoice.Number!, format),
                    EInvoiceSerializer.Write(piece, profile?.ToResponse(), format, preceding),
                    key),
                cancellationToken).ConfigureAwait(false)
            : await platform.ReportTransactionAsync(TransmitInvoiceHandler.TransactionOf(invoice.Seller!.Siren!, piece, invoice.Customer) with { IdempotencyKey = key }, cancellationToken)
                .ConfigureAwait(false);
        if (receipt.IsFailure)
        {
            return Result<PlatformTransmission>.Failure(receipt.Errors);
        }

        return new PlatformTransmission(
            Guid.CreateVersion7(),
            invoice.Id.Value,
            invoice.Number!,
            eInvoice ? PlatformChannel.EInvoice : PlatformChannel.EReporting,
            eInvoice ? format.ToDomain() : null,
            platform.Name,
            receipt.Value.Reference,
            piece.Totals.TotalInclTax,
            clock.GetUtcNow(),
            [receipt.Value.Status]);
    }
}

/// <summary>
/// Envoie un encaissement déclaré et l'enregistre avec son entrée au journal. Les contrôles de la déclaration sont
/// refaits à l'envoi (un autre encaissement a pu partir entre-temps) ; un essai repris après l'enregistrement le
/// reconnaît (même statut rendu par la plateforme sous la même clé) et ne journalise rien de plus.
/// </summary>
internal sealed class PlatformPaymentHandler(
    IInvoiceRepository invoices,
    IPlatformTransmissionStore transmissions,
    IAccreditedPlatformClient platform,
    IBillingTransaction transaction,
    IIntegrityJournal journal) : IOutboxHandler
{
    public string Kind => PlatformOutbox.PaymentKind;

    public async Task<OutboxOutcome> HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (PlatformOutbox.ReadPayment(delivery.Payload) is not { } order)
        {
            return OutboxOutcome.Poison("Déclaration d'encaissement illisible.");
        }

        Invoice? invoice = await invoices.FindAsync(new InvoiceId(order.InvoiceId), cancellationToken).ConfigureAwait(false);
        PlatformTransmission? transmission = invoice is null ? null
            : await transmissions.FindLatestAsync(invoice.Id.Value, cancellationToken).ConfigureAwait(false);
        if (invoice is null || transmission?.Reference is null || transmission.Id != order.TransmissionId || transmission.Status == PlatformStatus.Rejected)
        {
            return OutboxOutcome.Poison(ErrorCodes.Platform.NotTransmitted);
        }

        bool mayBeRecorded = transmission.History.Skip(order.KnownCount).Any(e => e.Status == PlatformStatus.Collected && e.Amount == order.Amount);
        if (!mayBeRecorded && transmission.IsFinal)
        {
            return OutboxOutcome.Poison(ErrorCodes.Platform.AlreadyFinal);
        }

        if (!mayBeRecorded && order.Amount > transmission.RemainingToCollect)
        {
            return OutboxOutcome.Poison(ErrorCodes.Platform.PaymentAmountInvalid);
        }

        Result<PlatformStatusEvent> collected = await platform.ReportPaymentAsync(
            new PlatformPaymentReport(invoice.Seller?.Siren ?? string.Empty, transmission.InvoiceNumber, transmission.Reference, transmission.Channel,
                order.Date, order.Amount, delivery.IdempotencyKey),
            cancellationToken).ConfigureAwait(false);
        if (collected.IsFailure)
        {
            return PlatformOutbox.Failed(collected.Errors);
        }

        if (transmission.Knows(collected.Value))
        {
            return OutboxOutcome.Delivered;
        }

        int known = transmission.History.Count;
        if (!transmission.TryApply(collected.Value))
        {
            return OutboxOutcome.Poison(ErrorCodes.Platform.AlreadyFinal);
        }

        Result recorded = await TransmitInvoiceHandler.RecordAsync(transmissions, transaction, journal, invoice, transmission, known, cancellationToken).ConfigureAwait(false);
        return recorded.IsFailure ? OutboxOutcome.Retry(recorded.Errors[0].Code) : OutboxOutcome.Delivered;
    }
}
