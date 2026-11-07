using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Dossiers;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Administration des dossiers d'un cabinet. Routes hors dossier : la permission porte sur le cabinet.</summary>
internal static class DossierEndpoints
{
    public static RouteGroupBuilder MapDossiers(this RouteGroupBuilder cabinet)
    {
        RouteGroupBuilder group = cabinet.MapGroup("/dossiers").WithTags("Dossiers");

        group.MapPost("/", CreateAsync)
            .RequireCabinetPermission(Permission.DossiersCreer)
            .WithName("CreateDossier")
            .Produces<DossierSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Effacement d'un dossier (administrateur du cabinet) : fermé aussitôt, annulable pendant le délai de rétractation.
        group.MapGet("/effacements", ListErasuresAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("ListDossierErasures")
            .Produces<IReadOnlyList<DossierErasureResponse>>();
        group.MapPost("/{dossierId:guid}/effacement", ScheduleErasureAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("ScheduleDossierErasure")
            .Produces<DossierErasureResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{dossierId:guid}/effacement", CancelErasureAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("CancelDossierErasure")
            .Produces<DossierSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    /// <summary>Les dossiers que l'utilisateur peut ouvrir, hors de tout dossier : chacun est filtré par ses droits.</summary>
    public static RouteGroupBuilder MapAccessibleDossiers(this RouteGroupBuilder dossiers)
    {
        dossiers.MapGet("/", ListAccessibleAsync)
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Dossiers")
            .WithName("ListAccessibleDossiers")
            .Produces<IReadOnlyList<DossierSummary>>();

        // Ce que l'utilisateur peut voir (écrans permis, accès à l'administration), sans dossier ouvert.
        dossiers.MapMyAccess();
        dossiers.MapCabinetOverview();
        dossiers.MapCabinetPilotage();
        dossiers.MapIntercompany();
        return dossiers;
    }

    private static async Task<IResult> ListAccessibleAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DossierSummary>> listed = await queries
            .QueryAsync(new ListAccessibleDossiersQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(listed);
    }

    private static async Task<IResult> CreateAsync(
        Guid cabinetId,
        CreateDossierRequest request,
        ICommandDispatcher commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<DossierSummary> created = await commands
            .SendAsync(new CreateDossierCommand(new OrganisationId(cabinetId), request.Name), cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? TypedResults.Created($"/api/v1/cabinets/{cabinetId}/dossiers/{created.Value.Id}", created.Value)
            : created.Errors.ToProblem();
    }

    private static async Task<IResult> ListErasuresAsync(Guid cabinetId, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListScheduledDossierErasuresQuery(new OrganisationId(cabinetId)), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<DossierErasureResponse> scheduled = await commands
            .SendAsync(new ScheduleDossierErasureCommand(new OrganisationId(cabinetId), new DossierId(dossierId)), cancellationToken).ConfigureAwait(false);
        return scheduled.IsSuccess
            ? TypedResults.Accepted($"/api/v1/cabinets/{cabinetId}/dossiers/effacements", scheduled.Value)
            : scheduled.Errors.ToProblem();
    }

    private static async Task<IResult> CancelErasureAsync(Guid cabinetId, Guid dossierId, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new CancelDossierErasureCommand(new OrganisationId(cabinetId), new DossierId(dossierId)), cancellationToken).ConfigureAwait(false));
}
