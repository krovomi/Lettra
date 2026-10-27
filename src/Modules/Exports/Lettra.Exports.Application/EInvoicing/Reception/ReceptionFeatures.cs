using System.Globalization;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Exports;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;

namespace Lettra.Exports.Application.EInvoicing.Reception;

/// <summary>Les factures fournisseurs reçues du dossier et la réception elle-même (plateforme, annuaire, dates).</summary>
public sealed record GetReceivedInvoicesQuery(DossierId Dossier) : IQuery<Result<ReceivedInvoicesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Une facture reçue en détail, relue dans le fichier reçu.</summary>
public sealed record GetReceivedInvoiceQuery(DossierId Dossier, Guid Id) : IQuery<Result<ReceivedInvoiceResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Le fichier reçu, octet pour octet (l'original de la facture).</summary>
public sealed record DownloadReceivedInvoiceQuery(DossierId Dossier, Guid Id) : IQuery<Result<EInvoiceFile>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Relever à la plateforme les factures mises à disposition de l'entreprise et enregistrer les nouvelles.</summary>
public sealed record FetchReceivedInvoicesCommand(DossierId Dossier) : ICommand<Result<FetchReceivedInvoicesResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FacturesRecuesTraiter;
}

/// <summary>Répondre au fournisseur, par la plateforme : prise en charge, approuvée, en litige, refusée, paiement transmis.</summary>
public sealed record RespondToReceivedInvoiceCommand(DossierId Dossier, Guid Id, RespondToReceivedInvoiceRequest Request)
    : ICommand<Result<ReceivedInvoiceResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FacturesRecuesTraiter;
}

/// <summary>Chercher une entreprise à l'annuaire de la facturation électronique.</summary>
public sealed record LookupDirectoryQuery(DossierId Dossier, string Siren) : IQuery<Result<DirectoryLookupResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

internal static class ReceptionMapping
{
    public static ReceivedInvoiceSummary ToSummary(this ReceivedInvoice invoice) => new(
        invoice.Id,
        invoice.Reference,
        (ReceivedInvoiceKindDto)(int)invoice.Header.Kind,
        invoice.Header.Number,
        invoice.Header.IssueDate,
        invoice.Header.DueDate,
        invoice.Header.SupplierName,
        invoice.Header.SupplierSiren,
        invoice.SupplierId,
        invoice.Header.TotalExclTax,
        invoice.Header.TotalVat,
        invoice.Header.TotalInclTax,
        invoice.Header.Currency,
        (PlatformStatusDto)(int)invoice.Status,
        invoice.ReceivedAt);

    public static ReceivedInvoiceResponse ToResponse(this ReceivedInvoice invoice)
    {
        ReadEInvoice? read = EInvoiceReader.Read(invoice.Format, invoice.Content);
        return new ReceivedInvoiceResponse(
            invoice.ToSummary(),
            invoice.Platform,
            (EInvoiceFormatDto)(int)invoice.Format,
            invoice.FileName,
            invoice.Header.SupplierVatNumber,
            invoice.Header.BusinessProcess,
            read?.Lines ?? [],
            read?.Vat ?? [],
            read?.Notes ?? [],
            [.. invoice.History.Select(e => new PlatformStatusEventResponse((PlatformStatusDto)(int)e.Status, e.At, e.Reason, e.Amount))],
            [.. invoice.AvailableResponses.Select(s => (PlatformStatusDto)(int)s)]);
    }

    public static DirectoryEntryResponse ToResponse(this DirectoryEntry entry) =>
        new(entry.Siren, entry.ElectronicAddress, entry.PlatformName, entry.Since);

    public static bool IsSiren(string? siren) => siren is { Length: 9 } && siren.All(char.IsAsciiDigit);
}

internal sealed class GetReceivedInvoicesHandler(
    IReceivedInvoiceStore store,
    ICompanyProfileRepository profiles,
    IAccreditedPlatformClient platform,
    IEInvoicingDirectory directory) : IQueryHandler<GetReceivedInvoicesQuery, Result<ReceivedInvoicesResponse>>
{
    public async Task<Result<ReceivedInvoicesResponse>> HandleAsync(GetReceivedInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        string? siren = profile?.Siren?.Value;
        bool configured = platform is not UnconfiguredPlatformClient;

        // L'annuaire injoignable ne cache pas les factures déjà reçues : l'inscription reste simplement inconnue.
        DirectoryEntry? entry = null;
        if (configured && siren is not null)
        {
            Result<DirectoryEntry?> found = await directory.LookupAsync(siren, cancellationToken).ConfigureAwait(false);
            entry = found.IsSuccess ? found.Value : null;
        }

        IReadOnlyList<ReceivedInvoice> invoices = await store.ListAsync(ReceivedInvoicesResponse.MaxInvoices, cancellationToken).ConfigureAwait(false);
        return new ReceivedInvoicesResponse(
            configured,
            siren,
            entry?.ToResponse(),
            EInvoicingCalendar.ReceivingFrom,
            EInvoicingCalendar.IssuingFrom(profile?.Size ?? CompanySize.Unknown),
            [.. invoices.Select(i => i.ToSummary())]);
    }
}

internal sealed class GetReceivedInvoiceHandler(IReceivedInvoiceStore store) : IQueryHandler<GetReceivedInvoiceQuery, Result<ReceivedInvoiceResponse>>
{
    public async Task<Result<ReceivedInvoiceResponse>> HandleAsync(GetReceivedInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ReceivedInvoice? invoice = await store.FindAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return invoice is null ? ReceptionErrors.NotFound : invoice.ToResponse();
    }
}

internal sealed class DownloadReceivedInvoiceHandler(IReceivedInvoiceStore store) : IQueryHandler<DownloadReceivedInvoiceQuery, Result<EInvoiceFile>>
{
    public async Task<Result<EInvoiceFile>> HandleAsync(DownloadReceivedInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ReceivedInvoice? invoice = await store.FindAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return invoice is null
            ? ReceptionErrors.NotFound
            : new EInvoiceFile(invoice.FileName, "application/xml", invoice.Content.ToArray());
    }
}

internal sealed class FetchReceivedInvoicesHandler(
    IReceivedInvoiceStore store,
    ICompanyProfileRepository profiles,
    IAccreditedPlatformClient platform,
    IUnitOfWork unitOfWork) : ICommandHandler<FetchReceivedInvoicesCommand, Result<FetchReceivedInvoicesResponse>>
{
    public async Task<Result<FetchReceivedInvoicesResponse>> HandleAsync(FetchReceivedInvoicesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        if (profile?.Siren?.Value is not { } siren)
        {
            return ReceptionErrors.CompanySirenMissing;
        }

        Result<IReadOnlyList<PlatformIncomingInvoice>> incoming = await platform.ListIncomingInvoicesAsync(siren, cancellationToken).ConfigureAwait(false);
        if (incoming.IsFailure)
        {
            return Result<FetchReceivedInvoicesResponse>.Failure(incoming.Errors);
        }

        IReadOnlySet<string> known = await store.KnownReferencesAsync([.. incoming.Value.Select(i => i.Reference)], cancellationToken).ConfigureAwait(false);
        List<(PlatformIncomingInvoice File, ReadEInvoice Read)> fresh = [];
        int unreadable = 0;
        foreach (PlatformIncomingInvoice file in incoming.Value.Where(i => !known.Contains(i.Reference)).DistinctBy(i => i.Reference))
        {
            // Une facture adressée à un autre SIREN n'est pas la nôtre : elle reste à la plateforme, comme un fichier illisible.
            if (EInvoiceReader.Read((EInvoiceFormat)(int)file.Format, file.Content) is { } read && read.Header.BuyerSiren == siren)
            {
                fresh.Add((file, read));
            }
            else
            {
                unreadable++;
            }
        }

        IReadOnlyDictionary<string, Guid> suppliers = await store
            .SuppliersBySirenAsync([.. fresh.Select(f => f.Read.Header.SupplierSiren).OfType<string>().Distinct(StringComparer.Ordinal)], cancellationToken)
            .ConfigureAwait(false);
        foreach ((PlatformIncomingInvoice file, ReadEInvoice read) in fresh)
        {
            await store.StageAsync(
                new ReceivedInvoice(
                    Guid.CreateVersion7(),
                    platform.Name,
                    file.Reference,
                    (EInvoiceFormat)(int)file.Format,
                    file.FileName,
                    file.Content,
                    read.Header,
                    read.Header.SupplierSiren is { } supplierSiren && suppliers.TryGetValue(supplierSiren, out Guid supplier) ? supplier : null,
                    [new PlatformStatusEvent(PlatformStatus.MadeAvailable, file.MadeAvailableAt)]),
                cancellationToken).ConfigureAwait(false);
        }

        if (fresh.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FetchReceivedInvoicesResponse(fresh.Count, unreadable);
    }
}

internal sealed class RespondToReceivedInvoiceHandler(
    IReceivedInvoiceStore store,
    IAccreditedPlatformClient platform,
    IUnitOfWork unitOfWork) : ICommandHandler<RespondToReceivedInvoiceCommand, Result<ReceivedInvoiceResponse>>
{
    public async Task<Result<ReceivedInvoiceResponse>> HandleAsync(RespondToReceivedInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReceivedInvoice? invoice = await store.FindAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return ReceptionErrors.NotFound;
        }

        PlatformStatus status = (PlatformStatus)(int)command.Request.Status;
        if (!invoice.AvailableResponses.Contains(status))
        {
            return ReceptionErrors.ResponseNotAllowed;
        }

        string? reason = string.IsNullOrWhiteSpace(command.Request.Reason) ? null : command.Request.Reason.Trim();
        if (ReceivedInvoice.RequiresReason(status) && reason is null)
        {
            return ReceptionErrors.ReasonRequired;
        }

        if (reason is { Length: > ReceivedInvoice.MaxReasonLength })
        {
            return ReceptionErrors.ReasonTooLong;
        }

        decimal? amount = status == PlatformStatus.PaymentSent ? command.Request.Amount : null;
        if (status == PlatformStatus.PaymentSent && amount is not > 0m)
        {
            return ReceptionErrors.PaymentAmountInvalid;
        }

        // Clé d'idempotence : la même réponse rejouée (réseau coupé après l'envoi) n'en crée pas une seconde.
        string key = string.Create(CultureInfo.InvariantCulture, $"{invoice.Id:N}-{invoice.History.Count}-{(int)status}");
        Result<PlatformStatusEvent> sent = await platform
            .RespondToIncomingInvoiceAsync(new PlatformBuyerResponse(invoice.Header.BuyerSiren, invoice.Reference, status, reason, amount, key), cancellationToken)
            .ConfigureAwait(false);
        if (sent.IsFailure)
        {
            return Result<ReceivedInvoiceResponse>.Failure(sent.Errors);
        }

        invoice.Apply(sent.Value);
        await store.StageAsync(invoice, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return invoice.ToResponse();
    }
}

internal sealed class LookupDirectoryHandler(IEInvoicingDirectory directory) : IQueryHandler<LookupDirectoryQuery, Result<DirectoryLookupResponse>>
{
    public async Task<Result<DirectoryLookupResponse>> HandleAsync(LookupDirectoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string siren = (query.Siren ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!ReceptionMapping.IsSiren(siren))
        {
            return ReceptionErrors.SirenInvalid;
        }

        // Sans plateforme, rien à consulter : ce n'est pas un refus, l'écran le dit simplement.
        if (directory is UnconfiguredDirectory)
        {
            return new DirectoryLookupResponse(siren, null, Consulted: false);
        }

        Result<DirectoryEntry?> found = await directory.LookupAsync(siren, cancellationToken).ConfigureAwait(false);
        return found.IsFailure
            ? Result<DirectoryLookupResponse>.Failure(found.Errors)
            : new DirectoryLookupResponse(siren, found.Value?.ToResponse());
    }
}
