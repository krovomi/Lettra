using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Features.Billing;
using Lettra.Application.Mapping;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Sync;
using Lettra.Domain.Billing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Sync;

/// <summary>
/// Recevoir au serveur une pièce validée hors ligne sur un poste (ADR-0007, ADR-0008). Le fait n'est ni fusionné ni
/// en conflit, mais il est revérifié : il doit porter exactement le numéro suivant de la série de ce poste, se
/// reconstruire avec les mêmes totaux, respecter les mentions obligatoires et redonner la même empreinte. Il entre
/// alors dans la chaîne d'intégrité du dossier et passe son écriture de vente, comme une validation au serveur.
/// </summary>
/// <param name="Dossier">Le dossier synchronisé.</param>
/// <param name="Fact">La pièce figée, telle que le poste l'a validée.</param>
/// <param name="DeviceId">Le poste qui envoie : seul le poste de la série peut y faire recevoir une pièce.</param>
public sealed record ImportOfflineInvoiceCommand(DossierId Dossier, Guid DeviceId, ValidatedInvoiceFact Fact)
    : ICommand<Result<SyncIntentOutcome>>, IRequirePermission
{
    /// <summary>Les droits du moment de la synchronisation : qui ne valide plus ne fait plus recevoir de pièce.</summary>
    public Permission Permission => Permission.FactureValider;
}

internal sealed class ImportOfflineInvoiceHandler(
    IInvoiceRepository invoices,
    IQuoteRepository quotes,
    ICustomerRepository customers,
    IDeviceSeriesRegistry deviceSeries,
    IIntegrityJournal journal,
    ISalesJournal salesJournal,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ImportOfflineInvoiceCommand, Result<SyncIntentOutcome>>
{
    public async Task<Result<SyncIntentOutcome>> HandleAsync(ImportOfflineInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        InvoiceResponse document = command.Fact.Document;
        if (document.Id != command.Fact.EntityId || document.Number is null || document.ContentHash is null
            || !document.SeriesCode.StartsWith(Invoice.Series + "-", StringComparison.Ordinal))
        {
            return BillingErrors.ReplicaMismatch;
        }

        // Réponse perdue après la réception : la pièce est déjà là, avec la même empreinte.
        Invoice? known = await invoices.FindAsync(new InvoiceId(document.Id), cancellationToken).ConfigureAwait(false);
        if (known is not null)
        {
            // Les brouillons ne se synchronisent pas encore : une pièce du même identifiant ne peut être que celle-ci.
            return !known.IsDraft && known.ContentHash == document.ContentHash ? SyncIntentOutcome.AlreadyApplied : BillingErrors.ReplicaMismatch;
        }

        if (await customers.FindAsync(new CustomerId(document.CustomerId), cancellationToken).ConfigureAwait(false) is null)
        {
            return SyncErrors.EntityNotFound;
        }

        return await deviceSeries.ExecuteWithDeviceNumberAsync(
            document.SeriesCode,
            command.DeviceId,
            document.Number,
            ct => ImportAsync(command.Fact, command.DeviceId, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<SyncIntentOutcome>> ImportAsync(ValidatedInvoiceFact fact, Guid deviceId, CancellationToken cancellationToken)
    {
        Result<Invoice> replicated = Invoice.Replicate(ToReplica(fact, deviceId));
        if (replicated.IsFailure)
        {
            return Result<SyncIntentOutcome>.Failure(replicated.Errors);
        }

        Invoice invoice = replicated.Value;
        ComplianceReport report = ComplianceChecker.Check(invoice.ToComplianceDocument(null), invoice.IssueDate!.Value);
        if (report.BlocksValidation)
        {
            return Result<SyncIntentOutcome>.Failure(
            [
                BillingErrors.NotCompliant,
                .. report.Errors.Select(f => Error.Validation(f.Code, f.Line is { } line ? $"Lines[{line - 1}]" : null)),
            ]);
        }

        // Même forme canonique, même empreinte : la pièce reçue est bien celle que le poste a remise au client.
        string hash = CanonicalDocument.ComputeHash(invoice.ToResponse());
        if (!string.Equals(hash, fact.Document.ContentHash, StringComparison.Ordinal))
        {
            return BillingErrors.ReplicaMismatch;
        }

        DateTimeOffset now = clock.GetUtcNow();
        invoice.Seal(hash, now);
        await invoices.AddAsync(invoice, cancellationToken).ConfigureAwait(false);
        Result effects = await InvoiceValidationEffects.ApplyToRelatedDocumentsAsync(invoice, invoices, quotes, user.UserId, now, cancellationToken)
            .ConfigureAwait(false);
        if (effects.IsFailure)
        {
            return Result<SyncIntentOutcome>.Failure(effects.Errors);
        }

        // Rechaînage au serveur après vérification (ADR-0007, tranché en phase 4) : une seule chaîne par dossier, où la
        // pièce du poste prend sa place à sa réception ; sa série est contrôlée à part, sans trou, comme FAC.
        Result<long> journaled = await journal.AppendAsync(
            InvoiceJournal.EventOf(invoice, invoice.Kind == InvoiceKind.CreditNote ? JournalEntryKind.CreditNoteValidated : JournalEntryKind.InvoiceValidated),
            cancellationToken).ConfigureAwait(false);
        if (journaled.IsFailure)
        {
            return Result<SyncIntentOutcome>.Failure(journaled.Errors);
        }

        await salesJournal.AddAsync(InvoiceValidationEffects.SalesEntryOf(invoice), cancellationToken).ConfigureAwait(false);
        return SyncIntentOutcome.Applied;
    }

    private static InvoiceReplica ToReplica(ValidatedInvoiceFact fact, Guid deviceId)
    {
        InvoiceResponse d = fact.Document;
        return new InvoiceReplica(
            new InvoiceId(d.Id),
            (InvoiceKind)(int)d.Kind,
            (InvoiceStatus)(int)d.Status,
            d.SeriesCode,
            deviceId,
            d.Number!,
            new CustomerId(d.CustomerId),
            Snapshot(d.Customer, fact.CustomerIsBusiness),
            d.Seller is { } seller ? Snapshot(seller, isBusiness: true) : throw new ArgumentException("Pièce validée sans émetteur.", nameof(fact)),
            new InvoiceHeader(d.DeliveryDate, d.DueDate, d.PaymentTerms, d.DeliveryAddress, d.PurchaseOrderReference, (VatRegime)(int)d.VatRegime, d.Subject, d.Notes),
            d.IssueDate ?? throw new ArgumentException("Pièce validée sans date.", nameof(fact)),
            fact.LatePenaltyRate,
            fact.EarlyPaymentDiscount,
            d.LegalMentions,
            d.QuoteId is { } quote ? new QuoteId(quote) : null,
            d.QuoteNumber,
            d.CreditedInvoiceId is { } credited ? new InvoiceId(credited) : null,
            d.CreditedInvoiceNumber,
            d.CreditReason,
            [.. d.Lines.OrderBy(l => l.Position).Select(Draft)],
            fact.DownPaymentPositions.ToHashSet(),
            d.Totals.TotalExclTax,
            d.Totals.TotalVat,
            d.Totals.TotalInclTax,
            d.CreatedBy,
            d.CreatedAt,
            d.ValidatedBy ?? throw new ArgumentException("Pièce validée sans auteur de la validation.", nameof(fact)),
            d.ValidatedAt ?? throw new ArgumentException("Pièce validée sans date de validation.", nameof(fact)),
            d.IssuedBy,
            d.IssuedAt,
            d.CreditApprovedBy,
            d.CreditApprovedAt);
    }

    private static PartySnapshot Snapshot(PartySnapshotResponse p, bool isBusiness) => new(
        p.Name, p.AddressLine1, p.AddressLine2, p.PostalCode, p.City, p.CountryCode, p.Siren, p.Siret, p.VatNumber, p.LegalForm, p.ShareCapital, p.Rcs, isBusiness);

    private static LineDraft Draft(DocumentLineResponse l) => new(
        l.CatalogItemId,
        l.Reference,
        l.Designation,
        l.Description,
        (CatalogItemKind)(int)l.Kind,
        l.UnitCode,
        l.Quantity,
        l.UnitPriceExclTax,
        l.DiscountPercent,
        l.VatRateCode,
        l.VatCategory == VatCategoryDto.Exempt ? l.VatExemptionCode : null);
}
