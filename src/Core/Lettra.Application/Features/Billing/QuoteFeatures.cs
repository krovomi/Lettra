using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Calculations.Compliance;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Lister les devis du dossier (recherche sur le numéro, l'objet et le nom du client).</summary>
public sealed record ListQuotesQuery(
    DossierId Dossier,
    string? Search = null,
    Guid? CustomerId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    QuoteStatusDto? Status = null,
    int Page = 1,
    int PageSize = 50,
    BillingSortDto Sort = BillingSortDto.IssueDate,
    SortDirectionDto Direction = SortDirectionDto.Descending,
    decimal? MinAmount = null,
    decimal? MaxAmount = null) : IQuery<Result<PagedResponse<QuoteSummary>>>, IRequirePermission
{
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.DossierLire;
}

/// <summary>Lire un devis.</summary>
public sealed record GetQuoteQuery(DossierId Dossier, QuoteId QuoteId)
    : IQuery<Result<QuoteResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Contrôler les mentions d'un devis (checklist de l'écran).</summary>
public sealed record CheckQuoteQuery(DossierId Dossier, QuoteId QuoteId)
    : IQuery<Result<ComplianceReportResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Créer un devis brouillon.</summary>
public sealed record CreateQuoteCommand(DossierId Dossier, SaveQuoteRequest Data)
    : ICommand<Result<QuoteId>>, IRequirePermission
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>Modifier un devis brouillon.</summary>
public sealed record UpdateQuoteCommand(DossierId Dossier, QuoteId QuoteId, SaveQuoteRequest Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>Supprimer un devis brouillon (un devis numéroté ne se supprime pas).</summary>
public sealed record DeleteQuoteCommand(DossierId Dossier, QuoteId QuoteId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>Valider un devis : numéro de la série DEV, copie du client, inscription au journal d'intégrité.</summary>
public sealed record ValidateQuoteCommand(DossierId Dossier, QuoteId QuoteId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>Enregistrer la réponse du client (acceptation ou refus) sur un devis validé.</summary>
public sealed record SetQuoteOutcomeCommand(DossierId Dossier, QuoteId QuoteId, bool Accepted, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.DevisCreer;
}

/// <summary>Créer une facture d'acompte brouillon sur un devis accepté (pourcentage ou montant TTC).</summary>
public sealed record CreateDownPaymentCommand(DossierId Dossier, QuoteId QuoteId, CreateDownPaymentRequest Data)
    : ICommand<Result<InvoiceId>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Créer la facture finale brouillon d'un devis accepté, acomptes validés déduits.</summary>
public sealed record CreateFinalInvoiceCommand(DossierId Dossier, QuoteId QuoteId)
    : ICommand<Result<InvoiceId>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

internal sealed class ListQuotesValidator : IValidator<ListQuotesQuery>
{
    public IReadOnlyList<Error> Validate(ListQuotesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListQuotesQuery.MaxPageSize)];
        errors.AddRange(BillingListing.Validate(instance.Sort, instance.Direction, instance.MinAmount, instance.MaxAmount));
        if (instance.Search is { Length: > ListQuotesQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        return errors;
    }
}

internal sealed class ListQuotesHandler(IQuoteReadStore store, TimeProvider clock)
    : IQueryHandler<ListQuotesQuery, Result<PagedResponse<QuoteSummary>>>
{
    public async Task<Result<PagedResponse<QuoteSummary>>> HandleAsync(ListQuotesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        BillingListFilter filter = new(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.CustomerId is { } customer ? new CustomerId(customer) : null,
            query.From,
            query.To,
            query.Page,
            query.PageSize,
            query.Sort,
            query.Direction,
            query.MinAmount,
            query.MaxAmount);
        return await store.ListAsync(filter, (QuoteStatus?)(int?)query.Status, LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetQuoteHandler(IQuoteRepository quotes, TimeProvider clock)
    : IQueryHandler<GetQuoteQuery, Result<QuoteResponse>>
{
    public async Task<Result<QuoteResponse>> HandleAsync(GetQuoteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Quote? quote = await quotes.FindAsync(query.QuoteId, cancellationToken).ConfigureAwait(false);
        return quote is null ? QuoteErrors.NotFound : quote.ToResponse(LegalCalendar.Today(clock));
    }
}

internal sealed class CheckQuoteHandler(IQuoteRepository quotes, ICompanyProfileRepository profiles, TimeProvider clock)
    : IQueryHandler<CheckQuoteQuery, Result<ComplianceReportResponse>>
{
    public async Task<Result<ComplianceReportResponse>> HandleAsync(CheckQuoteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Quote? quote = await quotes.FindAsync(query.QuoteId, cancellationToken).ConfigureAwait(false);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        SellerTerms? terms = await BillingLoads.SellerTermsAsync(profiles, cancellationToken).ConfigureAwait(false);
        return ComplianceChecker.Check(quote.ToComplianceDocument(terms), LegalCalendar.Today(clock)).ToResponse();
    }
}

internal sealed class CreateQuoteHandler(
    IQuoteRepository quotes,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateQuoteCommand, Result<QuoteId>>
{
    public async Task<Result<QuoteId>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<QuoteDraft> draft = await QuoteDrafts.BuildAsync(customers, command.Data, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure)
        {
            return Result<QuoteId>.Failure(draft.Errors);
        }

        Result<Quote> created = Quote.Create(QuoteId.New(), draft.Value, user.UserId, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<QuoteId>.Failure(created.Errors);
        }

        await quotes.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class UpdateQuoteHandler(
    IQuoteRepository quotes,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateQuoteCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Quote> loaded = Concurrency.Load(
            await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false), QuoteErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        Quote quote = loaded.Value;

        Result<QuoteDraft> draft = await QuoteDrafts.BuildAsync(customers, command.Data, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure)
        {
            return Result.Failure(draft.Errors);
        }

        Result updated = quote.Update(draft.Value, user.UserId, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class DeleteQuoteHandler(IQuoteRepository quotes, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteQuoteCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Quote> loaded = Concurrency.Load(
            await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false), QuoteErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        Quote quote = loaded.Value;

        Result deletable = quote.CanBeDeleted();
        if (deletable.IsFailure)
        {
            return deletable;
        }

        await quotes.RemoveAsync(quote, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>
/// Validation d'un devis : le numéro DEV est réservé, puis, dans la même transaction, le devis est figé et inscrit
/// au journal d'intégrité. Si l'inscription échoue, le numéro n'est pas consommé.
/// </summary>
internal sealed class ValidateQuoteHandler(
    IQuoteRepository quotes,
    ICustomerRepository customers,
    IDocumentNumbering numbering,
    IIntegrityJournal journal,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ValidateQuoteCommand, Result>
{
    public async Task<Result> HandleAsync(ValidateQuoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Contrôle préalable hors transaction : un refus évident ne réserve aucun numéro.
        Result<Quote> precheck = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        if (precheck.IsFailure)
        {
            return Result.Failure(precheck.Errors);
        }

        Result<bool> done = await numbering.ExecuteWithNextNumberAsync(
            Quote.Series,
            async (number, ct) =>
            {
                Result<Quote> loaded = await LoadAsync(command, ct).ConfigureAwait(false);
                if (loaded.IsFailure)
                {
                    return Result<bool>.Failure(loaded.Errors);
                }

                Quote quote = loaded.Value;
                DateTimeOffset now = clock.GetUtcNow();
                PartySnapshot customer = await BillingLoads.RefreshedCustomerAsync(customers, quote, ct).ConfigureAwait(false);
                Result validated = quote.Validate(number, LegalCalendar.Today(clock), customer, user.UserId, now);
                if (validated.IsFailure)
                {
                    return Result<bool>.Failure(validated.Errors);
                }

                string hash = CanonicalDocument.ComputeHash(quote.ToResponse(LegalCalendar.Today(clock)));
                Result<long> journaled = await journal.AppendAsync(
                    new DocumentLifecycleEvent(
                        JournalEntryKind.QuoteValidated,
                        JournaledDocumentKind.Quote,
                        quote.Id.Value,
                        quote.SeriesCode,
                        number,
                        quote.IssueDate!.Value,
                        quote.CustomerId.Value,
                        quote.TotalExclTax,
                        quote.TotalVat,
                        quote.TotalInclTax,
                        BillingJournal.Currency,
                        hash),
                    ct).ConfigureAwait(false);
                return journaled.IsFailure ? Result<bool>.Failure(journaled.Errors) : true;
            },
            cancellationToken).ConfigureAwait(false);
        return done.IsFailure ? Result.Failure(done.Errors) : Result.Success();
    }

    private async Task<Result<Quote>> LoadAsync(ValidateQuoteCommand command, CancellationToken cancellationToken)
    {
        Result<Quote> loaded = Concurrency.Load(
            await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false), QuoteErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result<Quote>.Failure(loaded.Errors);
        }

        Quote quote = loaded.Value;

        return quote.IsDraft ? quote : BillingErrors.NotDraft;
    }
}

internal sealed class SetQuoteOutcomeHandler(
    IQuoteRepository quotes,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetQuoteOutcomeCommand, Result>
{
    public async Task<Result> HandleAsync(SetQuoteOutcomeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<Quote> loaded = Concurrency.Load(
            await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false), QuoteErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        Quote quote = loaded.Value;

        Result result = quote.SetOutcome(command.Accepted, user.UserId, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class CreateDownPaymentValidator : IValidator<CreateDownPaymentCommand>
{
    public IReadOnlyList<Error> Validate(CreateDownPaymentCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        CreateDownPaymentRequest data = instance.Data;
        bool byPercent = data.Percent is > 0m and <= 100m;
        bool byAmount = data.AmountInclTax is > 0m;
        return byPercent ^ byAmount ? [] : [BillingErrors.DownPaymentInvalid];
    }
}

internal sealed class CreateDownPaymentHandler(
    IQuoteRepository quotes,
    IInvoiceRepository invoices,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateDownPaymentCommand, Result<InvoiceId>>
{
    public async Task<Result<InvoiceId>> HandleAsync(CreateDownPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Quote? quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        Result<decimal> share = quote.DownPaymentRatio(command.Data.Percent, command.Data.AmountInclTax);
        if (share.IsFailure)
        {
            return Result<InvoiceId>.Failure(share.Errors);
        }

        decimal ratio = share.Value;
        Result<Invoice> created = Invoice.CreateDownPayment(
            InvoiceId.New(),
            quote,
            ratio,
            command.Data.DueDate,
            BillingTexts.DownPaymentDesignation(quote, ratio),
            user.UserId,
            clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<InvoiceId>.Failure(created.Errors);
        }

        // Le plafond se vérifie sur le TTC réellement calculé (arrondis compris), les brouillons d'acompte exclus.
        Result allowed = quote.CanTakeDownPayment(created.Value.TotalInclTax);
        if (allowed.IsFailure)
        {
            return Result<InvoiceId>.Failure(allowed.Errors);
        }

        await invoices.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class CreateFinalInvoiceHandler(
    IQuoteRepository quotes,
    IInvoiceRepository invoices,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateFinalInvoiceCommand, Result<InvoiceId>>
{
    public async Task<Result<InvoiceId>> HandleAsync(CreateFinalInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Quote? quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        IReadOnlyList<Invoice> downPayments = await invoices.ListDownPaymentsAsync(quote.Id, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.GetUtcNow();
        Result<Invoice> created = Invoice.CreateFinal(InvoiceId.New(), quote, downPayments, BillingTexts.DeductionDesignation, user.UserId, now);
        if (created.IsFailure)
        {
            return Result<InvoiceId>.Failure(created.Errors);
        }

        Result attached = quote.AttachFinalInvoice(created.Value.Id, user.UserId, now);
        if (attached.IsFailure)
        {
            return Result<InvoiceId>.Failure(attached.Errors);
        }

        await invoices.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

/// <summary>Contenu d'un devis brouillon à partir de la saisie : le client est relu pour en figer une copie.</summary>
internal static class QuoteDrafts
{
    public static async Task<Result<QuoteDraft>> BuildAsync(ICustomerRepository customers, SaveQuoteRequest data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, data.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result<QuoteDraft>.Failure(customer.Errors);
        }

        return new QuoteDraft(
            new CustomerId(data.CustomerId),
            customer.Value,
            data.ValidUntil,
            (VatRegime)(int)data.VatRegime,
            data.Subject,
            data.Notes,
            data.Lines.ToDrafts());
    }
}

/// <summary>
/// Libellés générés des pièces dérivées. Ils sont imprimés sur des pièces légales, donc figés en français : la langue
/// de l'interface ne change pas le contenu d'une facture.
/// </summary>
internal static class BillingTexts
{
    public static string DownPaymentDesignation(Quote quote, decimal ratio) =>
        string.Create(
            System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
            $"Acompte de {ratio * 100m:0.##} % sur le devis n° {quote.Number}");

    public static string DeductionDesignation(Invoice downPayment) =>
        $"Déduction de l'acompte facture n° {downPayment.Number}";
}

/// <summary>Constantes communes des inscriptions au journal d'intégrité.</summary>
internal static class BillingJournal
{
    /// <summary>Lettra ne facture qu'en euros pour l'instant (code ISO 4217).</summary>
    public const string Currency = Money.Euro;
}
