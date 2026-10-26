using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Missions;
using Lettra.Domain.Common;
using Lettra.Domain.Missions;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Lettre de mission, honoraires et temps passé du dossier ouvert (en-tête <c>X-Dossier</c>).</summary>
internal static class MissionEndpoints
{
    public static RouteGroupBuilder MapMission(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/mission").WithTags("Cabinet");

        group.MapGet("/", GetAsync)
            .RequirePermission(Permission.DossierLire)
            .WithName("GetMission")
            .Produces<MissionOverviewResponse>();

        group.MapPut("/", SaveAsync)
            .RequirePermission(Permission.FactureValider)
            .WithName("SaveMission")
            .Produces<MissionOverviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireIfMatch();

        group.MapPost("/time", AddTimeAsync)
            .RequirePermission(Permission.FactureSaisir)
            .WithName("AddTimeEntry")
            .Produces<MissionOverviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/time/{id:guid}", DeleteTimeAsync)
            .RequirePermission(Permission.FactureSaisir)
            .WithName("DeleteTimeEntry")
            .Produces<MissionOverviewResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<IResult> GetAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetMissionOverviewQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SaveAsync(
        SaveMissionRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        Result saved = await commands.SendAsync(new SaveMissionCommand(dossier, request.ToDraft(), http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        Task<Result<MissionOverviewResponse>> Read() => queries.QueryAsync(new GetMissionOverviewQuery(dossier), cancellationToken);
        return saved.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(saved.Errors, Read, o => o.Mission?.Version ?? 0).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), o => o.Mission?.Version ?? 0);
    }

    private static async Task<IResult> AddTimeAsync(AddTimeEntryRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await commands
            .SendAsync(new AddTimeEntryCommand(tenant.Current.DossierId, request.Date, request.Minutes, (TimeTask)(int)request.Task, request.Note), cancellationToken)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteTimeAsync(Guid id, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new DeleteTimeEntryCommand(tenant.Current.DossierId, id), cancellationToken).ConfigureAwait(false));
}
