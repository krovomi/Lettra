using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Platform;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Administration de la plateforme (plan du mode 5, étape 2) : arrivée et départ des cabinets, relance des créations de
/// dossiers en échec. Réservé à l'administrateur de la plateforme, qui ne voit jamais les données d'un cabinet.
/// </summary>
internal static class PlatformEndpoints
{
    public static RouteGroupBuilder MapPlatform(this RouteGroupBuilder platform)
    {
        RouteGroupBuilder cabinets = platform.MapGroup("/cabinets").WithTags("Plateforme");

        cabinets.MapGet("/", ListAsync)
            .RequirePlatformPermission(Permission.PlateformeAdministrer)
            .WithName("ListPlatformCabinets")
            .Produces<PlatformOverviewResponse>();
        cabinets.MapPost("/", OnboardAsync)
            .RequirePlatformPermission(Permission.PlateformeAdministrer)
            .WithName("OnboardCabinet")
            .Produces<OnboardCabinetResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        cabinets.MapPost("/{organisationId:guid}/depart", ScheduleDepartureAsync)
            .RequirePlatformPermission(Permission.PlateformeAdministrer)
            .WithName("ScheduleCabinetDeparture")
            .Produces<PlatformCabinetResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        cabinets.MapDelete("/{organisationId:guid}/depart", CancelDepartureAsync)
            .RequirePlatformPermission(Permission.PlateformeAdministrer)
            .WithName("CancelCabinetDeparture")
            .Produces<PlatformCabinetResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        platform.MapPost("/dossiers/relance", RetryAsync)
            .RequirePlatformPermission(Permission.PlateformeAdministrer)
            .WithTags("Plateforme")
            .WithName("RetryFailedDossiers")
            .Produces<DossierRetryResponse>();
        return platform;
    }

    private static async Task<IResult> ListAsync(IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListPlatformCabinetsQuery(), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> OnboardAsync(OnboardCabinetRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<OnboardCabinetResponse> onboarded = await commands.SendAsync(new OnboardCabinetCommand(request), cancellationToken).ConfigureAwait(false);
        return onboarded.IsSuccess
            ? TypedResults.Created($"/api/v1/plateforme/cabinets/{onboarded.Value.Cabinet.Id}", onboarded.Value)
            : onboarded.Errors.ToProblem();
    }

    private static async Task<IResult> ScheduleDepartureAsync(Guid organisationId, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<PlatformCabinetResponse> scheduled = await commands
            .SendAsync(new ScheduleCabinetDepartureCommand(new OrganisationId(organisationId)), cancellationToken).ConfigureAwait(false);
        return scheduled.IsSuccess
            ? TypedResults.Accepted("/api/v1/plateforme/cabinets", scheduled.Value)
            : scheduled.Errors.ToProblem();
    }

    private static async Task<IResult> CancelDepartureAsync(Guid organisationId, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new CancelCabinetDepartureCommand(new OrganisationId(organisationId)), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RetryAsync(ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new RetryFailedDossiersCommand(), cancellationToken).ConfigureAwait(false));
}
