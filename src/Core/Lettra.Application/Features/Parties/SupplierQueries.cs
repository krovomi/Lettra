using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Parties;

/// <summary>Lister les fournisseurs du dossier, par page, filtrés sur le nom (sans accents ni casse).</summary>
public sealed record ListSuppliersQuery(DossierId Dossier, int Page = 1, int PageSize = 50, string? Search = null)
    : IQuery<Result<PagedResponse<SupplierSummary>>>, IRequirePermission
{
    public const int MaxSearchLength = 100;

    public const int MaxPageSize = 200;

    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>Lire un fournisseur.</summary>
public sealed record GetSupplierQuery(DossierId Dossier, SupplierId SupplierId)
    : IQuery<Result<SupplierResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

internal sealed class ListSuppliersValidator : IValidator<ListSuppliersQuery>
{
    public IReadOnlyList<Error> Validate(ListSuppliersQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [.. Paging.Validate(instance.Page, instance.PageSize, ListSuppliersQuery.MaxPageSize)];
        if (instance.Search is { Length: > ListSuppliersQuery.MaxSearchLength })
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.TooLong, nameof(instance.Search)));
        }

        return errors;
    }
}

internal sealed class ListSuppliersHandler(ISupplierReadStore store)
    : IQueryHandler<ListSuppliersQuery, Result<PagedResponse<SupplierSummary>>>
{
    public async Task<Result<PagedResponse<SupplierSummary>>> HandleAsync(ListSuppliersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.ListAsync(query.Search, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetSupplierHandler(ISupplierReadStore store)
    : IQueryHandler<GetSupplierQuery, Result<SupplierResponse>>
{
    public async Task<Result<SupplierResponse>> HandleAsync(GetSupplierQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        SupplierResponse? supplier = await store.GetAsync(query.SupplierId, cancellationToken).ConfigureAwait(false);
        return supplier is null ? SupplierErrors.NotFound : supplier;
    }
}

/// <summary>Contrôle commun des paramètres de pagination.</summary>
internal static class Paging
{
    public static IReadOnlyList<Error> Validate(int page, int pageSize, int maxPageSize)
    {
        List<Error> errors = [];
        if (page < 1)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, "Page"));
        }

        if (pageSize < 1 || pageSize > maxPageSize)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, "PageSize"));
        }

        return errors;
    }
}
