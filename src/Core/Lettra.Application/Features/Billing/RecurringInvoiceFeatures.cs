using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Parties;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Billing;

/// <summary>Lister les modèles de factures récurrentes, filtrés et triés.</summary>
public sealed record ListRecurringInvoicesQuery(
    DossierId Dossier,
    string? Search = null,
    Guid? CustomerId = null,
    bool? Active = null,
    bool DueOnly = false,
    RecurringInvoiceSortDto Sort = RecurringInvoiceSortDto.NextIssueDate,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50) : IQuery<Result<PagedResponse<RecurringInvoiceSummary>>>, IRequirePermission
{
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.DossierLire;
}

/// <summary>Lire un modèle, avec ses factures déjà créées.</summary>
public sealed record GetRecurringInvoiceQuery(DossierId Dossier, RecurringInvoiceId RecurringInvoiceId)
    : IQuery<Result<RecurringInvoiceResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Créer un modèle de facture récurrente.</summary>
public sealed record CreateRecurringInvoiceCommand(DossierId Dossier, SaveRecurringInvoiceRequest Data)
    : ICommand<Result<RecurringInvoiceId>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Modifier un modèle : les changements valent pour les périodes suivantes.</summary>
public sealed record UpdateRecurringInvoiceCommand(DossierId Dossier, RecurringInvoiceId RecurringInvoiceId, SaveRecurringInvoiceRequest Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand, ITargetsRecurringInvoice
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Suspendre ou reprendre un modèle.</summary>
public sealed record SetRecurringInvoiceActiveCommand(DossierId Dossier, RecurringInvoiceId RecurringInvoiceId, bool IsActive, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand, ITargetsRecurringInvoice
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Supprimer un modèle ; les factures qu'il a créées restent telles quelles.</summary>
public sealed record DeleteRecurringInvoiceCommand(DossierId Dossier, RecurringInvoiceId RecurringInvoiceId, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>
/// Créer les factures dues de tous les modèles actifs (ou d'un seul) : une par période échue, au plus
/// <see cref="RecurringInvoice.MaxCatchUp"/> par modèle et par passage. Une facture d'un modèle « validation
/// automatique » passe ensuite par <see cref="ValidateInvoiceCommand"/>, avec les droits et les contrôles de
/// l'utilisateur (ou du compte de service du Worker) : un refus la laisse en brouillon, sans bloquer les autres.
/// </summary>
public sealed record GenerateRecurringInvoicesCommand(DossierId Dossier, RecurringInvoiceId? OnlyTemplate = null)
    : ICommand<Result<RecurringRunResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureSaisir;
}

/// <summary>Commande qui porte sur un modèle : l'API relit le modèle après l'écriture.</summary>
public interface ITargetsRecurringInvoice
{
    RecurringInvoiceId RecurringInvoiceId { get; }
}

internal sealed class ListRecurringInvoicesValidator : IValidator<ListRecurringInvoicesQuery>
{
    public IReadOnlyList<Error> Validate(ListRecurringInvoicesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListRecurringInvoicesQuery.MaxPageSize)];
        if (instance.Search is { Length: > ListRecurringInvoicesQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        if (!Enum.IsDefined(instance.Sort))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Sort)));
        }

        return errors;
    }
}

internal sealed class ListRecurringInvoicesHandler(IRecurringInvoiceReadStore store, TimeProvider clock)
    : IQueryHandler<ListRecurringInvoicesQuery, Result<PagedResponse<RecurringInvoiceSummary>>>
{
    public async Task<Result<PagedResponse<RecurringInvoiceSummary>>> HandleAsync(ListRecurringInvoicesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        RecurringInvoiceListFilter filter = new(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.CustomerId is { } customer ? new CustomerId(customer) : null,
            query.Active,
            query.DueOnly,
            query.Sort,
            query.Descending,
            LegalCalendar.Today(clock),
            query.Page,
            query.PageSize);
        return await store.ListAsync(filter, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetRecurringInvoiceHandler(
    IRecurringInvoiceRepository templates,
    IRecurringInvoiceReadStore store,
    ICustomerRepository customers,
    TimeProvider clock) : IQueryHandler<GetRecurringInvoiceQuery, Result<RecurringInvoiceResponse>>
{
    public async Task<Result<RecurringInvoiceResponse>> HandleAsync(GetRecurringInvoiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        RecurringInvoice? template = await templates.FindAsync(query.RecurringInvoiceId, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return RecurringInvoiceErrors.NotFound;
        }

        Customer? customer = await customers.FindAsync(template.CustomerId, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<InvoiceId, RecurringInvoiceState> invoices = await store
            .InvoicesAsync([.. template.Occurrences.Select(o => o.InvoiceId)], cancellationToken).ConfigureAwait(false);
        return template.ToResponse(customer?.Name ?? string.Empty, invoices, LegalCalendar.Today(clock));
    }
}

internal sealed class CreateRecurringInvoiceHandler(
    IRecurringInvoiceRepository templates,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreateRecurringInvoiceCommand, Result<RecurringInvoiceId>>
{
    public async Task<Result<RecurringInvoiceId>> HandleAsync(CreateRecurringInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, command.Data.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result<RecurringInvoiceId>.Failure(customer.Errors);
        }

        Result<RecurringInvoice> created = RecurringInvoice.Create(RecurringInvoiceId.New(), command.Data.ToDraft(), user.UserId, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return Result<RecurringInvoiceId>.Failure(created.Errors);
        }

        await templates.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class UpdateRecurringInvoiceHandler(
    IRecurringInvoiceRepository templates,
    ICustomerRepository customers,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdateRecurringInvoiceCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateRecurringInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<RecurringInvoice> loaded = Concurrency.Load(
            await templates.FindAsync(command.RecurringInvoiceId, cancellationToken).ConfigureAwait(false), RecurringInvoiceErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        RecurringInvoice template = loaded.Value;

        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, command.Data.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result.Failure(customer.Errors);
        }

        Result result = template.Update(command.Data.ToDraft(), user.UserId, clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class SetRecurringInvoiceActiveHandler(
    IRecurringInvoiceRepository templates,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SetRecurringInvoiceActiveCommand, Result>
{
    public async Task<Result> HandleAsync(SetRecurringInvoiceActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<RecurringInvoice> loaded = Concurrency.Load(
            await templates.FindAsync(command.RecurringInvoiceId, cancellationToken).ConfigureAwait(false), RecurringInvoiceErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        RecurringInvoice template = loaded.Value;

        template.SetActive(command.IsActive, user.UserId, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class DeleteRecurringInvoiceHandler(IRecurringInvoiceRepository templates, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteRecurringInvoiceCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteRecurringInvoiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<RecurringInvoice> loaded = Concurrency.Load(
            await templates.FindAsync(command.RecurringInvoiceId, cancellationToken).ConfigureAwait(false), RecurringInvoiceErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        RecurringInvoice template = loaded.Value;

        await templates.RemoveAsync(template, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class GenerateRecurringInvoicesHandler(
    IRecurringInvoiceRepository templates,
    IInvoiceRepository invoices,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    ICommandDispatcher commands,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<GenerateRecurringInvoicesCommand, Result<RecurringRunResponse>>
{
    public async Task<Result<RecurringRunResponse>> HandleAsync(GenerateRecurringInvoicesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateOnly today = LegalCalendar.Today(clock);
        IReadOnlyList<RecurringInvoiceId> due = command.OnlyTemplate is { } only
            ? [only]
            : await templates.ListDueAsync(today, cancellationToken).ConfigureAwait(false);

        List<RecurringRunItem> created = [];
        List<RecurringRunFailure> failures = [];
        foreach (RecurringInvoiceId id in due)
        {
            for (int i = 0; i < RecurringInvoice.MaxCatchUp; i++)
            {
                // Relu à chaque période : la validation automatique vide le suivi des entités (numérotation).
                RecurringInvoice? template = await templates.FindAsync(id, cancellationToken).ConfigureAwait(false);
                if (template is null)
                {
                    if (command.OnlyTemplate is not null)
                    {
                        return RecurringInvoiceErrors.NotFound;
                    }

                    break;
                }

                if (template.DuePeriod(today) is null)
                {
                    break;
                }

                Result<RecurringRunItem> item = await GenerateOneAsync(command.Dossier, template, today, cancellationToken).ConfigureAwait(false);
                if (item.IsFailure)
                {
                    failures.Add(new RecurringRunFailure(id.Value, template.Name, item.Errors[0].Code));
                    break;
                }

                created.Add(item.Value);
            }
        }

        return new RecurringRunResponse(created, failures);
    }

    private async Task<Result<RecurringRunItem>> GenerateOneAsync(DossierId dossier, RecurringInvoice template, DateOnly today, CancellationToken cancellationToken)
    {
        Result<PartySnapshot> customer = await BillingLoads.CustomerSnapshotAsync(customers, template.CustomerId.Value, cancellationToken).ConfigureAwait(false);
        if (customer.IsFailure)
        {
            return Result<RecurringRunItem>.Failure(customer.Errors);
        }

        BillingPeriod period = template.DuePeriod(today)!;
        Result<Invoice> invoice = template.GenerateNext(InvoiceId.New(), customer.Value, today, user.UserId, clock.GetUtcNow());
        if (invoice.IsFailure)
        {
            return Result<RecurringRunItem>.Failure(invoice.Errors);
        }

        // La facture et la période consommée s'enregistrent ensemble : la clé (modèle, période) interdit un doublon.
        await invoices.AddAsync(invoice.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvoiceId invoiceId = invoice.Value.Id;
        bool validated = false;
        string? number = null;
        string? validationError = null;
        if (template.ValidateAutomatically)
        {
            Result validation = await commands.SendAsync(new ValidateInvoiceCommand(dossier, invoiceId, null), cancellationToken).ConfigureAwait(false);
            if (validation.IsSuccess)
            {
                validated = true;
                number = (await invoices.FindAsync(invoiceId, cancellationToken).ConfigureAwait(false))?.Number;
            }
            else
            {
                validationError = validation.Errors[0].Code;
            }
        }

        return new RecurringRunItem(template.Id.Value, template.Name, invoiceId.Value, period.Start, period.End, validated, number, validationError);
    }
}
