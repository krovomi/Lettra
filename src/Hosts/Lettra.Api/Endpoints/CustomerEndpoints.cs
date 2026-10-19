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

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("UpdateCustomer")
            .Produces<CustomerResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

        // Suppression : retiré des listes, gardé en base pour ses pièces (204 ; 412 si la version lue est périmée).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(Permission.TiersModifier)
            .WithName("DeleteCustomer")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireIfMatch();

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
            ? TypedResults.Created($"/api/v1/customers/{read.Value.Id}", read.Value).WithETag(read.Value.Version)
            : read.Errors.ToProblem();
    }

    private static async Task<IResult> ListAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 50,
        string? search = null)
    {
        Result<PagedResponse<CustomerSummary>> result = await queries
            .QueryAsync(new ListCustomersQuery(tenant.Current.DossierId, page, pageSize, string.IsNullOrWhiteSpace(search) ? null : search.Trim()), cancellationToken)
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
        return result.IsSuccess ? TypedResults.Ok(result.Value).WithETag(result.Value.Version) : result.Errors.ToProblem();
    }

    /// <summary>Modification : même contrat qu'à la création, version lue en <c>If-Match</c> (ADR-0013).</summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        CreateCustomerRequest request,
        HttpContext http,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        CustomerId customerId = new(id);
        Result updated = await commands
            .SendAsync(request.ToUpdateCommand(dossier, customerId, http.IfMatchVersion()), cancellationToken)
            .ConfigureAwait(false);
        Task<Result<CustomerResponse>> Read() => queries.QueryAsync(new GetCustomerQuery(dossier, customerId), cancellationToken);
        if (updated.IsFailure)
        {
            return await ConcurrencyResults.WriteFailureAsync(updated.Errors, Read, r => r.Version).ConfigureAwait(false);
        }

        Result<CustomerResponse> read = await Read().ConfigureAwait(false);
        return read.IsSuccess ? TypedResults.Ok(read.Value).WithETag(read.Value.Version) : read.Errors.ToProblem();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext http,
        ITenantContext tenant,
        ICommandDispatcher commands,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        DossierId dossier = tenant.Current.DossierId;
        CustomerId customerId = new(id);
        Result deleted = await commands.SendAsync(new DeleteCustomerCommand(dossier, customerId, http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        return deleted.IsSuccess
            ? TypedResults.NoContent()
            : await ConcurrencyResults.WriteFailureAsync(deleted.Errors, () => queries.QueryAsync(new GetCustomerQuery(dossier, customerId), cancellationToken), r => r.Version).ConfigureAwait(false);
    }
}
