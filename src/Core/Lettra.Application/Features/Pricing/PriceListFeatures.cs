using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Pricing;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Pricing;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Pricing;

/// <summary>Lister les grilles tarifaires du dossier.</summary>
public sealed record ListPriceListsQuery(DossierId Dossier, int Page = 1, int PageSize = 50)
    : IQuery<Result<PagedResponse<PriceListSummary>>>, IRequirePermission
{
    public const int MaxPageSize = Paging.MaxPageSize;

    public Permission Permission => Permission.CatalogueConsulter;
}

/// <summary>Lire une grille tarifaire.</summary>
public sealed record GetPriceListQuery(DossierId Dossier, PriceListId PriceListId)
    : IQuery<Result<PriceListResponse>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueConsulter;
}

/// <summary>
/// Prix proposés à la saisie d'un devis ou d'une facture pour ce client : prix de sa grille, sinon prix du catalogue.
/// Les articles inconnus sont ignorés.
/// </summary>
public sealed record ResolvePricesQuery(DossierId Dossier, Guid CustomerId, IReadOnlyCollection<Guid> CatalogItemIds)
    : IQuery<Result<IReadOnlyList<ResolvedPrice>>>, IRequirePermission
{
    public const int MaxItems = 500;

    public Permission Permission => Permission.CatalogueConsulter;
}

/// <summary>Créer une grille tarifaire. Un client rattaché quitte sa grille précédente.</summary>
public sealed record CreatePriceListCommand(DossierId Dossier, SavePriceListRequest Data)
    : ICommand<Result<PriceListId>>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Modifier une grille tarifaire.</summary>
public sealed record UpdatePriceListCommand(DossierId Dossier, PriceListId PriceListId, SavePriceListRequest Data, long? ExpectedVersion)
    : ICommand<Result>, IRequirePermission, IVersionedCommand
{
    public Permission Permission => Permission.CatalogueGerer;
}

/// <summary>Supprimer une grille : ses clients reviennent au prix du catalogue ; les pièces gardent leurs prix.</summary>
public sealed record DeletePriceListCommand(DossierId Dossier, PriceListId PriceListId)
    : ICommand<Result>, IRequirePermission
{
    public Permission Permission => Permission.CatalogueGerer;
}

internal sealed class ListPriceListsValidator : IValidator<ListPriceListsQuery>
{
    public IReadOnlyList<Error> Validate(ListPriceListsQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Paging.Validate(instance.Page, instance.PageSize, ListPriceListsQuery.MaxPageSize);
    }
}

internal sealed class ResolvePricesValidator : IValidator<ResolvePricesQuery>
{
    public IReadOnlyList<Error> Validate(ResolvePricesQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.CatalogItemIds is null || instance.CatalogItemIds.Count > ResolvePricesQuery.MaxItems
            ? [Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.CatalogItemIds))]
            : [];
    }
}

internal sealed class ListPriceListsHandler(IPriceListReadStore store)
    : IQueryHandler<ListPriceListsQuery, Result<PagedResponse<PriceListSummary>>>
{
    public async Task<Result<PagedResponse<PriceListSummary>>> HandleAsync(ListPriceListsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.ListAsync(query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetPriceListHandler(IPriceListReadStore store)
    : IQueryHandler<GetPriceListQuery, Result<PriceListResponse>>
{
    public async Task<Result<PriceListResponse>> HandleAsync(GetPriceListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PriceListResponse? list = await store.GetAsync(query.PriceListId, cancellationToken).ConfigureAwait(false);
        return list is null ? PriceListErrors.NotFound : list;
    }
}

internal sealed class ResolvePricesHandler(IPriceListRepository lists, IBillingReferenceChecker references)
    : IQueryHandler<ResolvePricesQuery, Result<IReadOnlyList<ResolvedPrice>>>
{
    public async Task<Result<IReadOnlyList<ResolvedPrice>>> HandleAsync(ResolvePricesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CatalogItemId[] ids = [.. query.CatalogItemIds.Distinct().Select(id => new CatalogItemId(id))];
        IReadOnlyDictionary<CatalogItemId, CatalogItem> items = await references.FindCatalogItemsAsync(ids, cancellationToken).ConfigureAwait(false);
        PriceList? list = query.CustomerId == Guid.Empty
            ? null
            : await lists.FindForCustomerAsync(new CustomerId(query.CustomerId), cancellationToken).ConfigureAwait(false);
        List<ResolvedPrice> prices = [];
        foreach (CatalogItemId id in ids)
        {
            if (!items.TryGetValue(id, out CatalogItem? item))
            {
                continue;
            }

            (decimal Price, decimal Discount)? special = list?.PriceFor(id, item.UnitPriceExclTax);
            prices.Add(new ResolvedPrice(
                id.Value,
                special?.Price ?? item.UnitPriceExclTax,
                special?.Discount ?? 0m,
                item.VatRate.Code,
                special is null ? null : list!.Id.Value));
        }

        return prices;
    }
}

internal sealed class CreatePriceListHandler(
    IPriceListRepository lists,
    IBillingReferenceChecker references,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<CreatePriceListCommand, Result<PriceListId>>
{
    public async Task<Result<PriceListId>> HandleAsync(CreatePriceListCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result known = await PriceListReferences.CheckAsync(references, command.Data, cancellationToken).ConfigureAwait(false);
        if (known.IsFailure)
        {
            return Result<PriceListId>.Failure(known.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result<PriceList> created = PriceList.Create(PriceListId.New(), command.Data.Name, command.Data.Entries.ToDrafts(), command.Data.CustomerIds, now);
        if (created.IsFailure)
        {
            return Result<PriceListId>.Failure(created.Errors);
        }

        await PriceListReferences.DetachFromOtherListsAsync(lists, created.Value, now, cancellationToken).ConfigureAwait(false);
        await lists.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created.Value.Id;
    }
}

internal sealed class UpdatePriceListHandler(
    IPriceListRepository lists,
    IBillingReferenceChecker references,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<UpdatePriceListCommand, Result>
{
    public async Task<Result> HandleAsync(UpdatePriceListCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<PriceList> loaded = Concurrency.Load(
            await lists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false), PriceListErrors.NotFound, command.ExpectedVersion);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Errors);
        }

        PriceList list = loaded.Value;

        Result result = await PriceListReferences.CheckAsync(references, command.Data, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result;
        }

        DateTimeOffset now = clock.GetUtcNow();
        result = list.Update(command.Data.Name, command.Data.Entries.ToDrafts(), command.Data.CustomerIds, now);
        if (result.IsFailure)
        {
            return result;
        }

        await PriceListReferences.DetachFromOtherListsAsync(lists, list, now, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal sealed class DeletePriceListHandler(IPriceListRepository lists, IUnitOfWork unitOfWork)
    : ICommandHandler<DeletePriceListCommand, Result>
{
    public async Task<Result> HandleAsync(DeletePriceListCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PriceList? list = await lists.FindAsync(command.PriceListId, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return PriceListErrors.NotFound;
        }

        await lists.RemoveAsync(list, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>Règles d'une grille qui dépassent l'agrégat : références existantes, un client dans une seule grille.</summary>
internal static class PriceListReferences
{
    public static async Task<Result> CheckAsync(IBillingReferenceChecker references, SavePriceListRequest data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        List<Error> errors = [];
        CustomerId[] customers = [.. data.CustomerIds.Distinct().Select(id => new CustomerId(id))];
        IReadOnlySet<CustomerId> existing = await references.ExistingCustomersAsync(customers, cancellationToken).ConfigureAwait(false);
        if (customers.Any(c => !existing.Contains(c)))
        {
            errors.Add(PriceListErrors.CustomerUnknown);
        }

        CatalogItemId[] items = [.. data.Entries.Select(e => new CatalogItemId(e.CatalogItemId)).Distinct()];
        IReadOnlyDictionary<CatalogItemId, CatalogItem> found = await references.FindCatalogItemsAsync(items, cancellationToken).ConfigureAwait(false);
        if (items.Any(i => !found.ContainsKey(i)))
        {
            errors.Add(PriceListErrors.CatalogItemUnknown);
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    public static async Task DetachFromOtherListsAsync(IPriceListRepository lists, PriceList list, DateTimeOffset now, CancellationToken cancellationToken)
    {
        CustomerId[] customers = [.. list.Customers.Select(c => c.CustomerId)];
        if (customers.Length == 0)
        {
            return;
        }

        foreach (PriceList other in await lists.ListContainingAsync(customers, list.Id, cancellationToken).ConfigureAwait(false))
        {
            foreach (CustomerId customer in customers)
            {
                other.RemoveCustomer(customer, now);
            }
        }
    }
}
