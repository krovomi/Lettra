using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Mapping;
using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>
/// La validation d'une facture, d'un acompte ou d'un avoir, en une seule transaction (annexe C, phase 3) :
/// <list type="number">
/// <item>réservation du numéro suivant de la série (mise à jour conditionnelle sur la version de la série) ;</item>
/// <item>gel de la pièce : copies de l'émetteur et du client, échéance, mentions légales ;</item>
/// <item>contrôle des mentions obligatoires : une erreur bloque, le numéro n'est pas consommé ;</item>
/// <item>empreinte SHA-256 de la forme canonique (<see cref="CanonicalDocument"/>) ;</item>
/// <item>inscription au journal d'intégrité chaîné ;</item>
/// <item>écriture de vente équilibrée (411 / 706-707-4191 / 44571 par taux) ;</item>
/// <item>effets sur les pièces liées : solde créditable de la facture d'origine, acomptes du devis.</item>
/// </list>
/// L'évènement <see cref="InvoiceValidated"/> part avec les changements (outbox du socle à venir).
/// <para>
/// Séparation des tâches du cabinet, contrôlée avant de réserver le numéro : ni l'auteur ni le dernier modificateur
/// du brouillon ne le valident, et un avoir au-delà du seuil exige l'approbation d'un manager, préalable ou donnée
/// par le manager qui le valide.
/// </para>
/// </summary>
internal sealed class ValidateInvoiceHandler(
    IInvoiceRepository invoices,
    IQuoteRepository quotes,
    ICustomerRepository customers,
    ICompanyProfileRepository profiles,
    IDocumentNumbering numbering,
    IIntegrityJournal journal,
    ISalesJournal salesJournal,
    ISegregationOfDuties duties,
    IDeviceNumbering deviceNumbering,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ValidateInvoiceCommand, Result>
{
    public async Task<Result> HandleAsync(ValidateInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Contrôles préalables hors transaction : un refus prévisible ne réserve aucun numéro.
        Result<(Invoice Invoice, CompanyProfile Profile)> precheck = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        if (precheck.IsFailure)
        {
            return Result.Failure(precheck.Errors);
        }

        Result<bool> approvesCredit = await CheckDutiesAsync(precheck.Value.Invoice, cancellationToken).ConfigureAwait(false);
        if (approvesCredit.IsFailure)
        {
            return Result.Failure(approvesCredit.Errors);
        }

        DateOnly today = LegalCalendar.Today(clock);
        ComplianceReport draftReport = ComplianceChecker.Check(precheck.Value.Invoice.ToComplianceDocument(precheck.Value.Profile.ToSellerTerms()), today);
        if (draftReport.BlocksValidation)
        {
            return Blocked(draftReport);
        }

        // Sur un poste hors ligne, la pièce se numérote dans la série du poste (ADR-0007) ; null sur le serveur.
        // Un poste révoqué, ou resté hors ligne au-delà de ce que le cabinet permet (étape 4.4), ne valide plus.
        Result<DeviceSeries?> onDevice = await deviceNumbering.FindAsync(precheck.Value.Invoice.SeriesCode, cancellationToken).ConfigureAwait(false);
        if (onDevice.IsFailure)
        {
            return Result.Failure(onDevice.Errors);
        }

        DeviceSeries? device = onDevice.Value;
        Result<bool> done = await numbering.ExecuteWithNextNumberAsync(
            device?.Code ?? precheck.Value.Invoice.SeriesCode,
            (number, ct) => ValidateWithNumberAsync(command, number, approvesCredit.Value, device, ct),
            cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? Result.Failure(done.Errors) : Result.Success();
    }

    private async Task<Result<bool>> ValidateWithNumberAsync(
        ValidateInvoiceCommand command, string number, bool approvesCredit, DeviceSeries? device, CancellationToken cancellationToken)
    {
        // Rejouable : tout est relu, le suivi des entités ayant été vidé par la numérotation.
        Result<(Invoice Invoice, CompanyProfile Profile)> loaded = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result<bool>.Failure(loaded.Errors);
        }

        (Invoice invoice, CompanyProfile profile) = loaded.Value;
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = LegalCalendar.Today(clock);
        PartySnapshot customer = await BillingLoads.RefreshedCustomerAsync(customers, invoice, cancellationToken).ConfigureAwait(false);
        if (approvesCredit)
        {
            invoice.ApproveCredit(user.UserId, now);
        }

        if (device is not null)
        {
            Result onDevice = invoice.NumberOnDevice(device.Code, device.DeviceId, user.UserId, now);
            if (onDevice.IsFailure)
            {
                return Result<bool>.Failure(onDevice.Errors);
            }
        }

        Result result = invoice.Validate(number, today, customer, profile.ToSellerTerms(), user.UserId, now);
        if (result.IsFailure)
        {
            return Result<bool>.Failure(result.Errors);
        }

        ComplianceReport report = ComplianceChecker.Check(invoice.ToComplianceDocument(null), today);
        if (report.BlocksValidation)
        {
            return Result<bool>.Failure(Blocked(report).Errors);
        }

        result = await InvoiceValidationEffects.ApplyToRelatedDocumentsAsync(invoice, invoices, quotes, user.UserId, now, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<bool>.Failure(result.Errors);
        }

        invoice.Seal(CanonicalDocument.ComputeHash(invoice.ToResponse()), now);
        Result<long> journaled = await journal.AppendAsync(
            InvoiceJournal.EventOf(invoice, invoice.Kind == InvoiceKind.CreditNote ? JournalEntryKind.CreditNoteValidated : JournalEntryKind.InvoiceValidated),
            cancellationToken).ConfigureAwait(false);
        if (journaled.IsFailure)
        {
            return Result<bool>.Failure(journaled.Errors);
        }

        await salesJournal.AddAsync(InvoiceValidationEffects.SalesEntryOf(invoice), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Contrôles de séparation des tâches. Vrai quand l'utilisateur, manager, approuve l'avoir en le validant.
    /// La version vérifiée à chaque relecture garantit que les auteurs contrôlés ici sont ceux de la pièce validée.
    /// </summary>
    private async Task<Result<bool>> CheckDutiesAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        Result issuer = await duties.EnsureCanIssueAsync([invoice.CreatedBy, invoice.LastModifiedBy], cancellationToken).ConfigureAwait(false);
        if (issuer.IsFailure)
        {
            return Result<bool>.Failure(issuer.Errors);
        }

        if (invoice.Kind != InvoiceKind.CreditNote || invoice.CreditApprovedBy is not null)
        {
            return false;
        }

        return await duties.EvaluateCreditNoteAsync(invoice.TotalInclTax, cancellationToken).ConfigureAwait(false) switch
        {
            CreditNoteApproval.ManagerApprovalRequired => SegregationOfDutiesErrors.ManagerApprovalRequired,
            CreditNoteApproval.GrantedByCurrentUser => true,
            _ => false,
        };
    }

    private async Task<Result<(Invoice Invoice, CompanyProfile Profile)>> LoadAsync(ValidateInvoiceCommand command, CancellationToken cancellationToken)
    {
        Result<Invoice> loaded = Concurrency.Load(
            await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false), InvoiceErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result<(Invoice, CompanyProfile)>.Failure(loaded.Errors);
        }

        Invoice invoice = loaded.Value;

        if (!invoice.IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        return profile is null ? BillingErrors.CompanyProfileMissing : (invoice, profile);
    }

    /// <summary>Le refus porte le code général puis une erreur par mention manquante (cible = ligne le cas échéant).</summary>
    private static Result Blocked(ComplianceReport report) =>
        Result.Failure(
        [
            BillingErrors.NotCompliant,
            .. report.Errors.Select(f => Error.Validation(f.Code, f.Line is { } line ? $"Lines[{line - 1}]" : null)),
        ]);
}

/// <summary>
/// Ce que la validation d'une pièce change ailleurs, partagé par la validation et par la réception au serveur d'une
/// pièce validée hors ligne. Rien n'est enregistré ici : tout s'exécute dans la transaction de l'appelant.
/// </summary>
internal static class InvoiceValidationEffects
{
    /// <summary>Solde créditable de la facture d'origine d'un avoir ; acomptes reçus sur le devis d'un acompte.</summary>
    public static async Task<Result> ApplyToRelatedDocumentsAsync(
        Invoice invoice, IInvoiceRepository invoices, IQuoteRepository quotes, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (invoice.Kind == InvoiceKind.CreditNote && invoice.CreditedInvoiceId is { } originalId)
        {
            Invoice? original = await invoices.FindAsync(originalId, cancellationToken).ConfigureAwait(false);
            return original is null ? InvoiceErrors.NotFound : original.RegisterCredit(invoice.TotalInclTax, userId, now);
        }

        if (invoice.Kind == InvoiceKind.DownPayment && invoice.QuoteId is { } quoteId)
        {
            Quote? quote = await quotes.FindAsync(quoteId, cancellationToken).ConfigureAwait(false);
            if (quote is null)
            {
                return QuoteErrors.NotFound;
            }

            Result allowed = quote.CanTakeDownPayment(invoice.TotalInclTax);
            if (allowed.IsFailure)
            {
                return allowed;
            }

            quote.RegisterDownPayment(invoice.TotalInclTax, userId, now);
        }

        return Result.Success();
    }

    /// <summary>L'écriture de vente de la pièce validée, calculée par la règle unique (<see cref="SalesEntryCalculator"/>).</summary>
    public static SalesJournalEntry SalesEntryOf(Invoice invoice)
    {
        IReadOnlyList<AccountingEntryLine> entry = SalesEntryCalculator.Compute(
            invoice.Kind switch
            {
                InvoiceKind.DownPayment => AccountingDocumentKind.DownPayment,
                InvoiceKind.CreditNote => AccountingDocumentKind.CreditNote,
                _ => AccountingDocumentKind.Invoice,
            },
            invoice.RevenueLines,
            invoice.ComputeTotals(),
            servicesVatOnReceipt: !invoice.VatOnDebits);
        return new SalesJournalEntry(invoice.Id, invoice.Number!, invoice.IssueDate!.Value, invoice.CustomerId, invoice.Customer.Name, entry);
    }
}

/// <summary>Inscription d'une pièce au journal d'intégrité, au format convenu avec la chaîne anti-fraude.</summary>
internal static class InvoiceJournal
{
    public static DocumentLifecycleEvent EventOf(Invoice invoice, JournalEntryKind kind) =>
        new(
            kind,
            invoice.Kind switch
            {
                InvoiceKind.DownPayment => JournaledDocumentKind.DepositInvoice,
                InvoiceKind.CreditNote => JournaledDocumentKind.CreditNote,
                _ => JournaledDocumentKind.Invoice,
            },
            invoice.Id.Value,
            invoice.SeriesCode,
            invoice.Number!,
            invoice.IssueDate!.Value,
            invoice.CustomerId.Value,
            invoice.TotalExclTax,
            invoice.TotalVat,
            invoice.TotalInclTax,
            BillingJournal.Currency,
            invoice.ContentHash!,
            invoice.CreditedInvoiceId?.Value);
}
