using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Lister les factures, acomptes et avoirs du dossier.</summary>
public sealed record ListInvoicesQuery(
    DossierId Dossier,
    string? Search = null,
    Guid? CustomerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    InvoiceKindDto? Kind = null,
    InvoiceStatusDto? Status = null,
    int Page = 1,
    int PageSize = 50) : IQuery<Result<PagedResponse<InvoiceSummary>>>, IRequirePermission
{
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.DossierLire;
}

/// <summary>Lire une facture, un acompte ou un avoir.</summary>
public sealed record GetInvoiceQuery(DossierId Dossier, InvoiceId InvoiceId)
    : IQuery<Result<InvoiceResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Contrôler les mentions obligatoires d'une pièce : checklist en direct sur un brouillon (avec les conditions de la
/// fiche entreprise qui s'appliqueront), verdict figé sur une pièce validée.
/// </summary>
public sealed record CheckInvoiceQuery(DossierId Dossier, InvoiceId InvoiceId)
    : IQuery<Result<ComplianceReportResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Créer une facture brouillon saisie librement.</summary>
public sealed record CreateInvoiceCommand(DossierId Dossier, SaveInvoiceRequest Data)
    : ICommand<Result<InvoiceId>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Modifier une facture brouillon.</summary>
public sealed record UpdateInvoiceCommand(DossierId Dossier, InvoiceId InvoiceId, SaveInvoiceRequest Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IInvoiceLifecycleCommand, IVersionedCommand
{
    public Permission Permission => Permission.FactureSaisir;

    InvoiceId IInvoiceLifecycleCommand.Invoice => InvoiceId;
}

/// <summary>Supprimer une pièce brouillon (une pièce numérotée ne se supprime jamais : on l'annule par avoir).</summary>
public sealed record DeleteInvoiceCommand(DossierId Dossier, InvoiceId InvoiceId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IInvoiceLifecycleCommand, IVersionedCommand
{
    public Permission Permission => Permission.FactureSaisir;

    InvoiceId IInvoiceLifecycleCommand.Invoice => InvoiceId;
}

/// <summary>Valider une pièce : numéro, gel, empreinte, journal d'intégrité et écriture de vente en une transaction.</summary>
public sealed record ValidateInvoiceCommand(DossierId Dossier, InvoiceId InvoiceId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IInvoiceLifecycleCommand, IVersionedCommand
{
    public Permission Permission => Permission.FactureValider;

    InvoiceId IInvoiceLifecycleCommand.Invoice => InvoiceId;
}

/// <summary>Marquer une pièce validée comme émise (envoyée au client).</summary>
public sealed record IssueInvoiceCommand(DossierId Dossier, InvoiceId InvoiceId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IInvoiceLifecycleCommand, IVersionedCommand
{
    public Permission Permission => Permission.FactureEmettre;

    InvoiceId IInvoiceLifecycleCommand.Invoice => InvoiceId;
}

/// <summary>Créer un avoir brouillon sur une pièce validée, en entier ou sur une partie de ses lignes.</summary>
public sealed record CreateCreditNoteCommand(DossierId Dossier, InvoiceId InvoiceId, CreateCreditNoteRequest Data)
    : ICommand<Result<InvoiceId>>, IRequirePermission, IInvoiceLifecycleCommand
{
    public Permission Permission => Permission.AvoirCreer;

    InvoiceId IInvoiceLifecycleCommand.Invoice => InvoiceId;
}

/// <summary>
/// Commande qui fait avancer une pièce : point d'accroche des contrôles transverses (séparation des tâches,
/// verrou optimiste) sans connaître chaque commande.
/// </summary>
public interface IInvoiceLifecycleCommand
{
    InvoiceId Invoice { get; }
}

internal sealed class ListInvoicesValidator : IValidator<ListInvoicesQuery>
{
    public IReadOnlyList<Error> Validate(ListInvoicesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListInvoicesQuery.MaxPageSize)];
        if (instance.Search is { Length: > ListInvoicesQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        return errors;
    }
}

internal sealed class ListInvoicesHandler(IInvoiceReadStore store)
    : IQueryHandler<ListInvoicesQuery, Result<PagedResponse<InvoiceSummary>>>
{
    public async Task<Result<PagedResponse<InvoiceSummary>>> HandleAsync(ListInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingListFilter filter = new(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.CustomerId is { } customer ? new CustomerId(customer) : null,
            query.From,
            query.To,
            query.Page,
            query.PageSize);
        return await store.ListAsync(filter, (InvoiceKind?)(int?)query.Kind, (InvoiceStatus?)(int?)query.Status, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class GetInvoiceHandler(IInvoiceRepository invoices)
    : IQueryHandler<GetInvoiceQuery, Result<InvoiceResponse>>
{
    public async Task<Result<InvoiceResponse>> HandleAsync(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Invoice? invoice = await invoices.FindAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        return invoice is null ? InvoiceErrors.NotFound : invoice.ToResponse();
    }
}

internal sealed class CheckInvoiceHandler(IInvoiceRepository invoices, ICompanyProfileRepository profiles, TimeProvider clock)
    : IQueryHandler<CheckInvoiceQuery, Result<ComplianceReportResponse>>
{
    public async Task<Result<ComplianceReportResponse>> HandleAsync(CheckInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Invoice? invoice = await invoices.FindAsync(query.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        SellerTerms? terms = invoice.IsDraft ? await BillingLoads.SellerTermsAsync(profiles, cancellationToken).ConfigureAwait(false) : null;
        return ComplianceChecker.Check(invoice.ToComplianceDocument(terms), BillingCalendar.Today(clock)).ToResponse();
    }
}

internal sealed class CreateInvoiceHandler(
    IInvoiceRepository invoices,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateInvoiceCommand, Result<InvoiceId>>
{
    public async Task<Result<InvoiceId>> HandleAsync(CreateInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, command.Data.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result<InvoiceId>.Failure(customer.Errors);
        }

        Result<Invoice> created = Invoice.Create(
            InvoiceId.New(),
            new CustomerId(command.Data.CustomerId),
            customer.Value,
            command.Data.ToHeader(),
            command.Data.Lines.ToDrafts(),
            user.UserId,
            clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<InvoiceId>.Failure(created.Errors);
        }

        await invoices.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class UpdateInvoiceHandler(
    IInvoiceRepository invoices,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateInvoiceCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result result = Concurrency.Check(invoice, command.ExpectedVersion);
        if (result.IsFailure)
        {
            return result;
        }

        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, command.Data.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result.Failure(customer.Errors);
        }

        result = invoice.Update(
            new CustomerId(command.Data.CustomerId),
            customer.Value,
            command.Data.ToHeader(),
            command.Data.Lines.ToDrafts(),
            user.UserId,
            clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class DeleteInvoiceHandler(
    IInvoiceRepository invoices,
    IQuoteRepository quotes,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<DeleteInvoiceCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result version = Concurrency.Check(invoice, command.ExpectedVersion);
        if (version.IsFailure)
        {
            return version;
        }

        if (!invoice.IsDraft)
        {
            return BillingErrors.NotDraft;
        }

        // Supprimer la facture finale brouillon rend le devis à nouveau facturable.
        if (invoice.Kind == InvoiceKind.Invoice && invoice.QuoteId is { } quoteId
            && await quotes.FindAsync(quoteId, cancellationToken).ConfigureAwait(false) is { } quote)
        {
            quote.DetachFinalInvoice(invoice.Id, user.UserId, clock.GetUtcNow());
        }

        await invoices.RemoveAsync(invoice, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class IssueInvoiceHandler(
    IInvoiceRepository invoices,
    Abstractions.Integrity.IIntegrityJournal journal,
    IBillingTransaction transaction,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<IssueInvoiceCommand, Result>
{
    // L'émission et son entrée au journal inaltérable s'enregistrent ensemble : sans transaction ouverte, le journal
    // enregistrerait seul et repartirait d'un suivi des modifications vide.
    public Task<Result> HandleAsync(IssueInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return transaction.ExecuteAsync(token => IssueAsync(command, token), cancellationToken);
    }

    private async Task<Result> IssueAsync(IssueInvoiceCommand command, CancellationToken cancellationToken)
    {
        Invoice? invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        Result result = Concurrency.Check(invoice, command.ExpectedVersion);
        if (result.IsFailure)
        {
            return result;
        }

        result = invoice.Issue(user.UserId, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        Result<long> journaled = await journal.AppendAsync(
            InvoiceJournal.EventOf(invoice, invoice.Kind == InvoiceKind.CreditNote
                ? Domain.Integrity.JournalEntryKind.CreditNoteIssued
                : Domain.Integrity.JournalEntryKind.InvoiceIssued),
            cancellationToken).ConfigureAwait(false);
        return journaled.IsFailure ? Result.Failure(journaled.Errors) : Result.Success();
    }
}

internal sealed class CreateCreditNoteHandler(
    IInvoiceRepository invoices,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateCreditNoteCommand, Result<InvoiceId>>
{
    public async Task<Result<InvoiceId>> HandleAsync(CreateCreditNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Invoice? original = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);
        if (original is null)
        {
            return InvoiceErrors.NotFound;
        }

        Dictionary<int, decimal>? quantities = null;
        if (command.Data.Lines is { Count: > 0 } lines)
        {
            if (lines.Select(l => l.Position).Distinct().Count() != lines.Count)
            {
                return InvoiceErrors.CreditLineUnknown;
            }

            quantities = lines.ToDictionary(l => l.Position, l => l.Quantity);
        }

        Result<Invoice> created = Invoice.CreateCreditNote(InvoiceId.New(), original, quantities, command.Data.Reason, user.UserId, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<InvoiceId>.Failure(created.Errors);
        }

        await invoices.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}
