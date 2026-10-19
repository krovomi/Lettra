using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Cabinet;
using Lettra.Domain.Common;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Vue cabinet : tableau de supervision des dossiers (hors de tout dossier, chaque dossier filtré par les droits) et
/// revue de dossier (dans le dossier ouvert).
/// </summary>
internal static class CabinetOverviewEndpoints
{
    /// <summary>Sous <c>/api/v1/dossiers</c> : pas de dossier choisi, la vue liste ceux que l'utilisateur peut ouvrir.</summary>
    public static RouteGroupBuilder MapCabinetOverview(this RouteGroupBuilder dossiers)
    {
        dossiers.MapGet("/overview", GetOverviewAsync)
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Cabinet")
            .WithName("GetCabinetOverview")
            .Produces<CabinetOverviewResponse>();

        // Recalcul immédiat (bouton « Actualiser ») : tous les dossiers accessibles, ou un seul avec ?dossier=.
        dossiers.MapPost("/overview/refresh", RefreshAsync)
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Cabinet")
            .WithName("RefreshCabinetOverview")
            .Produces<CabinetOverviewRefreshResponse>();
        return dossiers;
    }

    /// <summary>Dans le dossier ouvert (en-tête <c>X-Dossier</c>) : la revue par période.</summary>
    public static RouteGroupBuilder MapDossierReview(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/review").WithTags("Cabinet");

        group.MapGet("/{period}", GetReviewAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetDossierReview")
            .Produces<DossierReviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{period}/items/{item}", SetItemAsync)
            .RequirePermission(Permission.FactureSaisir)
            .WithName("SetDossierReviewItem")
            .Produces<DossierReviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<IResult> GetOverviewAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<CabinetOverviewResponse> overview = await queries.QueryAsync(new GetCabinetOverviewQuery(), cancellationToken).ConfigureAwait(false);
        return overview.IsSuccess ? TypedResults.Ok(overview.Value) : overview.Errors.ToProblem();
    }

    private static async Task<IResult> RefreshAsync(Guid? dossier, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<CabinetOverviewRefreshResponse> refreshed = await commands
            .SendAsync(new RefreshCabinetOverviewCommand(dossier is Guid one ? [one] : null), cancellationToken).ConfigureAwait(false);
        return refreshed.IsSuccess ? TypedResults.Ok(refreshed.Value) : refreshed.Errors.ToProblem();
    }

    private static async Task<IResult> GetReviewAsync(string period, ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<DossierReviewResponse> review = await queries
            .QueryAsync(new GetDossierReviewQuery(tenant.Current.DossierId, period), cancellationToken).ConfigureAwait(false);
        return review.IsSuccess ? TypedResults.Ok(review.Value) : review.Errors.ToProblem();
    }

    private static async Task<IResult> SetItemAsync(
        string period,
        string item,
        SetDossierReviewItemRequest request,
        ITenantContext tenant,
        ICommandDispatcher commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<DossierReviewResponse> review = await commands
            .SendAsync(new SetDossierReviewItemCommand(tenant.Current.DossierId, period, item, request.Checked, request.Note), cancellationToken)
            .ConfigureAwait(false);
        return review.IsSuccess ? TypedResults.Ok(review.Value) : review.Errors.ToProblem();
    }
}
