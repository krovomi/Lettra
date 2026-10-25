using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Lister les changements d'IBAN du dossier, à vérifier d'abord par défaut (filtre d'état, recherche, tri).</summary>
public sealed record ListIbanChangesQuery(
    DossierId Dossier,
    IbanChangeStatusDto? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50,
    IbanChangeSortDto Sort = IbanChangeSortDto.ChangedAt,
    SortDirectionDto Direction = SortDirectionDto.Descending)
    : IQuery<Result<PagedResponse<IbanChangeResponse>>>, IRequirePermission
{
    public const int MaxSearchLength = 100;

    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>L'historique des changements d'IBAN d'un fournisseur, du plus récent au plus ancien.</summary>
public sealed record ListSupplierIbanChangesQuery(DossierId Dossier, SupplierId SupplierId)
    : IQuery<Result<IReadOnlyList<IbanChangeResponse>>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>Confirmer un nouvel IBAN, vérifié auprès du fournisseur par un autre canal que la demande.</summary>
public sealed record ConfirmIbanChangeCommand(DossierId Dossier, IbanChangeId ChangeId, IbanVerificationMethodDto? Method, string? Note, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

/// <summary>Refuser un nouvel IBAN jugé suspect.</summary>
public sealed record RejectIbanChangeCommand(DossierId Dossier, IbanChangeId ChangeId, string? Note, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.TiersModifier;
}

internal sealed class ListIbanChangesValidator : IValidator<ListIbanChangesQuery>
{
    public IReadOnlyList<Error> Validate(ListIbanChangesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, Paging.MaxPageSize)];
        if (instance.Search is { Length: > ListIbanChangesQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        if (instance.Status is { } status && !Enum.IsDefined(status))
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Status)));
        }

        errors.AddRange(ListSortRules.Validate(instance.Sort, instance.Direction));
        return errors;
    }
}

internal sealed class ListIbanChangesHandler(IIbanChangeReadStore store)
    : IQueryHandler<ListIbanChangesQuery, Result<PagedResponse<IbanChangeResponse>>>
{
    public async Task<Result<PagedResponse<IbanChangeResponse>>> HandleAsync(ListIbanChangesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store
            .ListAsync(new IbanChangeListFilter(query.Status, ListSearch.Normalize(query.Search), query.Page, query.PageSize, query.Sort, query.Direction), cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class ListSupplierIbanChangesHandler(IIbanChangeReadStore store)
    : IQueryHandler<ListSupplierIbanChangesQuery, Result<IReadOnlyList<IbanChangeResponse>>>
{
    public async Task<Result<IReadOnlyList<IbanChangeResponse>>> HandleAsync(ListSupplierIbanChangesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Result<IReadOnlyList<IbanChangeResponse>>.Success(await store.ForSupplierAsync(query.SupplierId, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed class ConfirmIbanChangeHandler(IbanChangeDecisions decisions) : ICommandHandler<ConfirmIbanChangeCommand, Result>
{
    public Task<Result> HandleAsync(ConfirmIbanChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return decisions.DecideAsync(
            command.ChangeId,
            command.ExpectedVersion,
            (change, user, now) => command.Method is { } method
                ? change.Confirm((IbanVerificationMethod)method, command.Note, user, now)
                : IbanChangeErrors.MethodRequired,
            cancellationToken);
    }
}

internal sealed class RejectIbanChangeHandler(IbanChangeDecisions decisions) : ICommandHandler<RejectIbanChangeCommand, Result>
{
    public Task<Result> HandleAsync(RejectIbanChangeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return decisions.DecideAsync(command.ChangeId, command.ExpectedVersion, (change, user, now) => change.Reject(command.Note, user, now), cancellationToken);
    }
}

/// <summary>
/// Ce que partagent la saisie d'un nouvel IBAN et les décisions : le changement à vérifier (le précédent encore en attente
/// est remplacé) et l'alerte de sécurité du dossier, levée tant qu'un changement attend, résolue quand plus aucun n'attend.
/// </summary>
public sealed class IbanChangeDecisions(
    IIbanChangeRepository changes,
    IUnitOfWork unitOfWork,
    ISegregationOfDuties duties,
    ICurrentUser user,
    ITenantContext tenant,
    IDossierAlertPublisher alerts,
    TimeProvider clock)
{
    /// <summary>Enregistre le changement détecté à la modification du fournisseur (avant l'enregistrement de celle-ci).</summary>
    public async Task<bool> TrackAsync(Supplier supplier, Iban? previous, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        DateTimeOffset now = clock.GetUtcNow();
        IbanChange? change = IbanChange.Detect(supplier.Id, previous, supplier.Iban, user.UserId, now);
        if (change is null)
        {
            return false;
        }

        foreach (IbanChange older in await changes.PendingForAsync(supplier.Id, cancellationToken).ConfigureAwait(false))
        {
            older.Supersede(now);
        }

        await changes.AddAsync(change, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Après l'enregistrement : l'alerte du dossier suit le nombre de changements à vérifier.</summary>
    public async Task PublishAsync(CancellationToken cancellationToken)
    {
        int pending = await changes.CountPendingAsync(cancellationToken).ConfigureAwait(false);
        await alerts.PublishAsync(
            new DossierAlertSignal(DossierAlertRule.SupplierIbanToVerify, tenant.Current.OrganisationId, tenant.Current.DossierId, pending > 0, clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Result> DecideAsync(
        IbanChangeId id, long? expectedVersion, Func<IbanChange, Guid, DateTimeOffset, Result> decide, CancellationToken cancellationToken)
    {
        Result<IbanChange> loaded = Concurrency.Load(
            await changes.FindAsync(id, cancellationToken).ConfigureAwait(false), IbanChangeErrors.NotFound, expectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        IbanChange change = loaded.Value;
        Result allowed = await duties.EnsureCanDecideIbanChangeAsync(change.ChangedBy, cancellationToken).ConfigureAwait(false);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        Result decided = decide(change, user.UserId, clock.GetUtcNow());
        if (decided.IsFailure)
        {
            return decided;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PublishAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
