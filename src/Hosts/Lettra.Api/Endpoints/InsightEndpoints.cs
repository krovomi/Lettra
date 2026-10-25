using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Insights;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Éclairages du dossier (module Exports) : des lectures calculées en C# déterministe sur les pièces et les encaissements,
/// sans rien écrire. D'abord la météo des paiements : un bulletin par client et la prévision des encaissements.
/// </summary>
internal static class InsightEndpoints
{
    public static RouteGroupBuilder MapInsights(this RouteGroupBuilder api)
    {
        RouteGroupBuilder insights = api.MapGroup("/insights").WithTags("Insights");

        insights.MapGet("/payment-weather", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPaymentWeatherQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.DossierLire)
            .WithName("GetPaymentWeather")
            .Produces<PaymentWeatherResponse>();

        return insights;
    }

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
