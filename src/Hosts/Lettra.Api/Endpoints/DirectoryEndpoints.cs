using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;

namespace Lettra.Api.Endpoints;

/// <summary>Annuaires publics (SIRENE, VIES), consultés à la demande de l'utilisateur pendant une saisie.</summary>
internal static class DirectoryEndpoints
{
    public static RouteGroupBuilder MapDirectory(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/directory").WithTags("Directory");

        group.MapGet("/companies", SearchAsync)
            .WithName("SearchCompanies")
            .Produces<CompanySearchResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/vat-numbers/{vatNumber}", CheckVatAsync)
            .WithName("CheckVatNumber")
            .Produces<VatNumberCheckResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> SearchAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        string q = "",
        int max = 10)
    {
        Result<CompanySearchResponse> result = await queries
            .QueryAsync(new SearchCompaniesQuery(tenant.Current.DossierId, q, max), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> CheckVatAsync(string vatNumber, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<VatNumberCheckResponse> result = await queries
            .QueryAsync(new CheckVatNumberQuery(tenant.Current.DossierId, vatNumber), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
