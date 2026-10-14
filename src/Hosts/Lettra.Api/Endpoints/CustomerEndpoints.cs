using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Customers;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>Routes du module Tiers. Une route = un cas d'usage = une permission (portée par la commande).</summary>
internal static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomers(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/customers").WithTags("Customers");

        group.MapPost("/", CreateAsync)
            .WithName("CreateCustomer")
            .RequirePermission(Permission.TiersCreer)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListAsync)
            .WithName("ListCustomers")
            .RequirePermission(Permission.TiersConsulter)
            .Produces<PagedResponse<CustomerSummary>>();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetCustomer")
            .RequirePermission(Permission.TiersConsulter)
            .Produces<CustomerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> CreateAsync(
        CreateCustomerRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        Result<CustomerId> created = await commands.SendAsync(request.ToCommand(dossier), cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Errors.ToProblem();
        }

        Result<CustomerResponse> read = await queries.QueryAsync(new GetCustomerQuery(dossier, created.Value), cancellationToken).ConfigureAwait(false);
        return read.IsSuccess
            ? TypedResults.Created($"/api/v1/customers/{read.Value.Id}", read.Value)
            : read.Errors.ToProblem();
    }

    private static async Task<IResult> ListAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 50)
    {
        Result<PagedResponse<CustomerSummary>> result = await queries
            .QueryAsync(new ListCustomersQuery(tenant.Current.DossierId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        Result<CustomerResponse> result = await queries
            .QueryAsync(new GetCustomerQuery(tenant.Current.DossierId, new CustomerId(id)), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
