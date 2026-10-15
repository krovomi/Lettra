using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;

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
/// </summary>
internal sealed class ValidateInvoiceHandler(
    IInvoiceRepository invoices,
    IQuoteRepository quotes,
    ICustomerRepository customers,
    ICompanyProfileRepository profiles,
    IDocumentNumbering numbering,
    IIntegrityJournal journal,
    ISalesJournal salesJournal,
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

        DateOnly today = BillingCalendar.Today(clock);
        ComplianceReport draftReport = ComplianceChecker.Check(precheck.Value.Invoice.ToComplianceDocument(precheck.Value.Profile.ToSellerTerms()), today);
        if (draftReport.BlocksValidation)
        {
            return Blocked(draftReport);
        }

        Result<bool> done = await numbering.ExecuteWithNextNumberAsync(
            precheck.Value.Invoice.SeriesCode,
            (number, ct) => ValidateWithNumberAsync(command, number, ct),
            cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? Result.Failure(done.Errors) : Result.Success();
    }

    private async Task<Result<bool>> ValidateWithNumberAsync(ValidateInvoiceCommand command, string number, CancellationToken cancellationToken)
    {
        // Rejouable : tout est relu, le suivi des entités ayant été vidé par la numérotation.
        Result<(Invoice Invoice, CompanyProfile Profile)> loaded = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return Result<bool>.Failure(loaded.Errors);
        }

        (Invoice invoice, CompanyProfile profile) = loaded.Value;
        DateTimeOffset now = clock.GetUtcNow();
        DateOnly today = BillingCalendar.Today(clock);
        PartySnapshot customer = await BillingLoads.RefreshedCustomerAsync(customers, invoice, cancellationToken).ConfigureAwait(false);
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

        result = await ApplyToRelatedDocumentsAsync(invoice, now, cancellationToken).ConfigureAwait(false);
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

        IReadOnlyList<AccountingEntryLine> entry = SalesEntryCalculator.Compute(
            invoice.Kind switch
            {
                InvoiceKind.DownPayment => AccountingDocumentKind.DownPayment,
                InvoiceKind.CreditNote => AccountingDocumentKind.CreditNote,
                _ => AccountingDocumentKind.Invoice,
            },
            invoice.RevenueLines,
            invoice.ComputeTotals());
        await salesJournal.AddAsync(
            new SalesJournalEntry(invoice.Id, number, today, invoice.CustomerId, invoice.Customer.Name, entry),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<Result> ApplyToRelatedDocumentsAsync(Invoice invoice, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (invoice.Kind == InvoiceKind.CreditNote && invoice.CreditedInvoiceId is { } originalId)
        {
            Invoice? original = await invoices.FindAsync(originalId, cancellationToken).ConfigureAwait(false);
            return original is null ? InvoiceErrors.NotFound : original.RegisterCredit(invoice.TotalInclTax, user.UserId, now);
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

            quote.RegisterDownPayment(invoice.TotalInclTax, user.UserId, now);
        }

        return Result.Success();
    }

    private async Task<Result<(Invoice Invoice, CompanyProfile Profile)>> LoadAsync(ValidateInvoiceCommand command, CancellationToken cancellationToken)
    {
        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result version = invoice.EnsureVersion(command.ExpectedVersion);
        if (version.IsFailure)
        {
            return Result<(Invoice, CompanyProfile)>.Failure(version.Errors);
        }

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
