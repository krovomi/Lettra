using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Billing;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// Transmettre une pièce validée à la plateforme agréée : facture électronique (CII ou UBL) pour un client professionnel
/// établi en France, e-reporting de la transaction sinon. Une fois la pièce déposée, elle est émise (même contrôle de
/// séparation des tâches que l'émission). Rejouer la commande rend la transmission en cours ; seule une pièce rejetée
/// repart.
/// </summary>
public sealed record TransmitInvoiceCommand(DossierId Dossier, InvoiceId InvoiceId, EInvoiceFormatDto Format = EInvoiceFormatDto.Cii)
    : ICommand<Result<PlatformTransmissionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Relire à la plateforme les statuts d'une pièce transmise et ajouter ceux que le cycle de vie permet.</summary>
public sealed record RefreshPlatformStatusCommand(DossierId Dossier, InvoiceId InvoiceId)
    : ICommand<Result<PlatformTransmissionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Déclarer un encaissement (statut Encaissée), partiel ou total, dans la limite du TTC restant.</summary>
public sealed record ReportPaymentCommand(DossierId Dossier, InvoiceId InvoiceId, DateOnly Date, decimal Amount)
    : ICommand<Result<PlatformTransmissionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>La dernière transmission d'une pièce et l'historique de ses statuts, tels qu'enregistrés dans le dossier.</summary>
public sealed record GetPlatformTransmissionQuery(DossierId Dossier, InvoiceId InvoiceId)
    : IQuery<Result<PlatformTransmissionResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

internal sealed class TransmitInvoiceHandler(
    IInvoiceRepository invoices,
    ICompanyProfileRepository profiles,
    IPlatformTransmissionStore transmissions,
    IAccreditedPlatformClient platform,
    ISegregationOfDuties duties,
    ICommandDispatcher commands,
    TimeProvider clock) : ICommandHandler<TransmitInvoiceCommand, Result<PlatformTransmissionResponse>>
{
    public async Task<Result<PlatformTransmissionResponse>> HandleAsync(TransmitInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Format))
        {
            return EInvoiceErrors.FormatUnknown;
        }

        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.IsDraft || invoice.Number is null || invoice.Seller is null)
        {
            return EInvoiceErrors.NotValidated;
        }

        PlatformTransmission? current = await transmissions.FindLatestAsync(invoice.Id.Value, cancellationToken).ConfigureAwait(false);
        if (current is not null && current.Status != PlatformStatusDto.Rejected)
        {
            return await IssuedAsync(command, invoice, current, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(invoice.Seller.Siren))
        {
            return PlatformErrors.SellerSirenMissing;
        }

        // Avant l'envoi : une pièce déposée que son auteur ne pourrait pas émettre resterait à moitié transmise.
        if (invoice.Status == InvoiceStatus.Validated)
        {
            Result allowed = await duties.EnsureCanIssueAsync([invoice.CreatedBy], cancellationToken).ConfigureAwait(false);
            if (allowed.IsFailure)
            {
                return Result<PlatformTransmissionResponse>.Failure(allowed.Errors);
            }
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        InvoiceResponse piece = invoice.ToResponse();
        bool eInvoice = IsDomesticBusiness(invoice.Customer);
        Result<PlatformReceipt> receipt = eInvoice
            ? await platform.SubmitInvoiceAsync(
                new PlatformInvoiceSubmission(
                    invoice.Seller.Siren,
                    invoice.Customer.Siren!,
                    invoice.Number,
                    command.Format,
                    EInvoiceSerializer.FileNameOf(invoice.Number, command.Format),
                    EInvoiceSerializer.Write(piece, profile?.ToResponse(), command.Format)),
                cancellationToken).ConfigureAwait(false)
            : await platform.ReportTransactionAsync(TransactionOf(invoice.Seller.Siren, piece, invoice.Customer), cancellationToken).ConfigureAwait(false);
        if (receipt.IsFailure)
        {
            return Result<PlatformTransmissionResponse>.Failure(receipt.Errors);
        }

        PlatformTransmission transmission = new(
            Guid.CreateVersion7(),
            invoice.Id.Value,
            invoice.Number,
            eInvoice ? PlatformChannelDto.EInvoice : PlatformChannelDto.EReporting,
            eInvoice ? command.Format : null,
            platform.Name,
            receipt.Value.Reference,
            piece.Totals.TotalInclTax,
            clock.GetUtcNow(),
            [receipt.Value.Status]);
        await transmissions.SaveAsync(transmission, cancellationToken).ConfigureAwait(false);
        return await IssuedAsync(command, invoice, transmission, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Une pièce déposée (non rejetée) est émise ; l'émission garde ses propres contrôles et son journal.</summary>
    private async Task<Result<PlatformTransmissionResponse>> IssuedAsync(
        TransmitInvoiceCommand command, Invoice invoice, PlatformTransmission transmission, CancellationToken cancellationToken)
    {
        if (invoice.Status == InvoiceStatus.Validated && transmission.Status != PlatformStatusDto.Rejected)
        {
            Result issued = await commands.SendAsync(new IssueInvoiceCommand(command.Dossier, command.InvoiceId, null), cancellationToken).ConfigureAwait(false);
            if (issued.IsFailure)
            {
                return Result<PlatformTransmissionResponse>.Failure(issued.Errors);
            }
        }

        return transmission.ToResponse();
    }

    /// <summary>E-invoicing : un professionnel établi en France, identifié par son SIREN. Le reste relève de l'e-reporting.</summary>
    internal static bool IsDomesticBusiness(PartySnapshot customer) =>
        customer.IsBusiness
        && string.Equals(customer.CountryCode, "FR", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(customer.Siren);

    private static PlatformTransactionReport TransactionOf(string sellerSiren, InvoiceResponse piece, PartySnapshot customer)
    {
        EInvoice model = EInvoice.From(piece, null);
        return new PlatformTransactionReport(
            sellerSiren,
            model.Number,
            model.IssueDate,
            model.IsCreditNote,
            customer.IsBusiness ? PlatformTransactionKind.International : PlatformTransactionKind.Consumer,
            model.Buyer.CountryCode,
            model.Currency,
            [.. model.Vat.Select(v => new PlatformVatAmount(v.Category, v.Rate, v.Basis, v.Amount))],
            model.TotalExclTax,
            model.TotalVat,
            model.TotalInclTax);
    }
}

internal sealed partial class RefreshPlatformStatusHandler(
    IPlatformTransmissionStore transmissions,
    IAccreditedPlatformClient platform,
    ILogger<RefreshPlatformStatusHandler> logger) : ICommandHandler<RefreshPlatformStatusCommand, Result<PlatformTransmissionResponse>>
{
    public async Task<Result<PlatformTransmissionResponse>> HandleAsync(RefreshPlatformStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PlatformTransmission? transmission = await transmissions.FindLatestAsync(command.InvoiceId.Value, cancellationToken).ConfigureAwait(false);
        if (transmission is null)
        {
            return PlatformErrors.NotTransmitted;
        }

        if (transmission.IsFinal || transmission.Reference is null)
        {
            return transmission.ToResponse();
        }

        Result<IReadOnlyList<PlatformStatusEvent>> events = await platform.GetStatusEventsAsync(transmission.Reference, cancellationToken).ConfigureAwait(false);
        if (events.IsFailure)
        {
            return Result<PlatformTransmissionResponse>.Failure(events.Errors);
        }

        bool changed = false;
        foreach (PlatformStatusEvent status in events.Value.OrderBy(e => e.At))
        {
            if (transmission.TryApply(status))
            {
                changed = true;
            }
            else if (!transmission.History.Any(known => known.Status == status.Status && known.At.ToUnixTimeMilliseconds() == status.At.ToUnixTimeMilliseconds()))
            {
                LogIgnored(logger, transmission.Reference, (int)status.Status, (int)transmission.Status);
            }
        }

        if (changed)
        {
            await transmissions.SaveAsync(transmission, cancellationToken).ConfigureAwait(false);
        }

        return transmission.ToResponse();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Plateforme agréée : statut {Status} ignoré pour {Reference}, hors cycle de vie depuis {Current}.")]
    private static partial void LogIgnored(ILogger logger, string reference, int status, int current);
}

internal sealed class ReportPaymentHandler(
    IInvoiceRepository invoices,
    IPlatformTransmissionStore transmissions,
    IAccreditedPlatformClient platform) : ICommandHandler<ReportPaymentCommand, Result<PlatformTransmissionResponse>>
{
    public async Task<Result<PlatformTransmissionResponse>> HandleAsync(ReportPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        PlatformTransmission? transmission = await transmissions.FindLatestAsync(invoice.Id.Value, cancellationToken).ConfigureAwait(false);
        if (transmission?.Reference is null || transmission.Status == PlatformStatusDto.Rejected)
        {
            return PlatformErrors.NotTransmitted;
        }

        if (transmission.IsFinal)
        {
            return PlatformErrors.AlreadyFinal;
        }

        if (command.Amount <= 0m || command.Amount > transmission.RemainingToCollect)
        {
            return PlatformErrors.PaymentAmountInvalid;
        }

        if (invoice.IssueDate is DateOnly issued && command.Date < issued)
        {
            return PlatformErrors.PaymentDateInvalid;
        }

        Result<PlatformStatusEvent> collected = await platform.ReportPaymentAsync(
            new PlatformPaymentReport(invoice.Seller?.Siren ?? string.Empty, transmission.InvoiceNumber, transmission.Reference, transmission.Channel, command.Date, command.Amount),
            cancellationToken).ConfigureAwait(false);
        if (collected.IsFailure)
        {
            return Result<PlatformTransmissionResponse>.Failure(collected.Errors);
        }

        if (transmission.TryApply(collected.Value))
        {
            await transmissions.SaveAsync(transmission, cancellationToken).ConfigureAwait(false);
        }

        return transmission.ToResponse();
    }
}

internal sealed class GetPlatformTransmissionHandler(IPlatformTransmissionStore transmissions)
    : IQueryHandler<GetPlatformTransmissionQuery, Result<PlatformTransmissionResponse>>
{
    public async Task<Result<PlatformTransmissionResponse>> HandleAsync(GetPlatformTransmissionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PlatformTransmission? transmission = await transmissions.FindLatestAsync(query.InvoiceId.Value, cancellationToken).ConfigureAwait(false);
        return transmission is null ? PlatformErrors.NotTransmitted : transmission.ToResponse();
    }
}
