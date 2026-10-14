using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Customers;

/// <summary>Lister les clients du dossier, par page.</summary>
public sealed record ListCustomersQuery(DossierId Dossier, int Page = 1, int PageSize = 50)
    : IQuery<Result<PagedResponse<CustomerSummary>>>, IRequirePermission
{
    public const int MaxPageSize = 200;

    public Permission Permission => Permission.TiersConsulter;
}

/// <summary>Lire un client.</summary>
public sealed record GetCustomerQuery(DossierId Dossier, CustomerId CustomerId)
    : IQuery<Result<CustomerResponse>>, IRequirePermission
{
    public Permission Permission => Permission.TiersConsulter;
}

internal sealed class ListCustomersValidator : IValidator<ListCustomersQuery>
{
    public IReadOnlyList<Error> Validate(ListCustomersQuery instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        List<Error> errors = [];
        if (instance.Page < 1)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.Page)));
        }

        if (instance.PageSize is < 1 or > ListCustomersQuery.MaxPageSize)
        {
            errors.Add(Error.Validation(ErrorCodes.Validation.Invalid, nameof(instance.PageSize)));
        }

        return errors;
    }
}

internal sealed class ListCustomersHandler(ICustomerReadStore store)
    : IQueryHandler<ListCustomersQuery, Result<PagedResponse<CustomerSummary>>>
{
    public async Task<Result<PagedResponse<CustomerSummary>>> HandleAsync(ListCustomersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.ListAsync(query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GetCustomerHandler(ICustomerReadStore store)
    : IQueryHandler<GetCustomerQuery, Result<CustomerResponse>>
{
    public async Task<Result<CustomerResponse>> HandleAsync(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        CustomerResponse? customer = await store.GetAsync(query.CustomerId, cancellationToken).ConfigureAwait(false);
        return customer is null ? CustomerErrors.NotFound : customer;
    }
}
