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
/// <param name="ReleasedBy">
/// Le manager qui a débloqué la série du poste pour ce numéro (étape 4.4) : la pièce est reçue sous son autorité, sans
/// revérifier le droit de valider, l'approbation de l'avoir ni les mentions. Suite des numéros, poste et empreinte restent
/// vérifiés.
/// </param>
public sealed record ImportOfflineInvoiceCommand(DossierId Dossier, Guid DeviceId, ValidatedInvoiceFact Fact, Guid? ReleasedBy = null)
    : ICommand<Result<SyncIntentOutcome>>, IRequirePermission
{
    /// <summary>
    /// Les droits du moment de la synchronisation : qui ne valide plus ne fait plus recevoir de pièce, sauf déblocage par
    /// un manager (qui avait <see cref="Permission.PostesGerer"/>).
    /// </summary>
    public Permission Permission => ReleasedBy is null ? Permission.FactureValider : Permission.DossierLire;
}

internal sealed class ImportOfflineInvoiceHandler(
    IInvoiceRepository invoices,
    IQuoteRepository quotes,
    ICustomerRepository customers,
    IDeviceSeriesRegistry deviceSeries,
    ISegregationOfDuties duties,
    IIntegrityJournal journal,
    ISalesJournal salesJournal,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ImportOfflineInvoiceCommand, Result<SyncIntentOutcome>>
{
    public async Task<Result<SyncIntentOutcome>> HandleAsync(ImportOfflineInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        InvoiceResponse document = command.Fact.Document;
        // Donnée venue du poste : une pièce incomplète est refusée, jamais une exception.
        if (document.Id != command.Fact.EntityId || document.Number is null || document.ContentHash is null
            || document.Seller is null || document.IssueDate is null || document.ValidatedBy is null || document.ValidatedAt is null
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

        // Séparation des tâches revérifiée avec la politique du moment (#33) : le seuil connu du poste a pu changer.
        if (command.ReleasedBy is null && document.Kind == InvoiceKindDto.CreditNote && document.CreditApprovedBy is null
            && await duties.EvaluateCreditNoteAsync(document.Totals.TotalInclTax, cancellationToken).ConfigureAwait(false) == CreditNoteApproval.ManagerApprovalRequired)
        {
            return SegregationOfDutiesErrors.ManagerApprovalRequired;
        }

        return await deviceSeries.ExecuteWithDeviceNumberAsync(
            document.SeriesCode,
            command.DeviceId,
            document.Number,
            ct => ImportAsync(command.Fact, command.DeviceId, released: command.ReleasedBy is not null, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<SyncIntentOutcome>> ImportAsync(ValidatedInvoiceFact fact, Guid deviceId, bool released, CancellationToken cancellationToken)
    {
        Result<Invoice> replicated = Invoice.Replicate(ToReplica(fact, deviceId));
        if (replicated.IsFailure)
        {
            return Result<SyncIntentOutcome>.Failure(replicated.Errors);
        }

        Invoice invoice = replicated.Value;
        ComplianceReport report = ComplianceChecker.Check(invoice.ToComplianceDocument(null), invoice.IssueDate!.Value);
        // Une pièce débloquée a été remise au client telle quelle : elle se corrige par un avoir, pas en la refusant.
        if (report.BlocksValidation && !released)
        {
            return Result<SyncIntentOutcome>.Failure(InvoiceValidationEffects.Blocked(report).Errors);
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
        Result recorded = await InvoiceValidationEffects.RecordAsync(invoice, journal, salesJournal, cancellationToken).ConfigureAwait(false);
        return recorded.IsFailure ? Result<SyncIntentOutcome>.Failure(recorded.Errors) : SyncIntentOutcome.Applied;
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
            Snapshot(d.Seller!, isBusiness: true),
            new InvoiceHeader(d.DeliveryDate, d.DueDate, d.PaymentTerms, d.DeliveryAddress, d.PurchaseOrderReference, (VatRegime)(int)d.VatRegime, d.Subject, d.Notes),
            d.IssueDate!.Value,
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
            d.ValidatedBy!.Value,
            d.ValidatedAt!.Value,
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
