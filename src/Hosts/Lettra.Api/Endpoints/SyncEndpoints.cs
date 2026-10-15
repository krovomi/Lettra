using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Sync;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Sync.Server;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Synchronisation des postes hors ligne (phase 4, ADR-0008). La route n'exige que d'ouvrir le dossier : chaque intention
/// est ensuite rejouée par son cas d'usage, qui vérifie sa propre permission ; la réception ne rend que ce que
/// l'utilisateur peut lire.
/// </summary>
internal static class SyncEndpoints
{
    public static RouteGroupBuilder MapSync(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/sync").WithTags("Sync");

        group.MapPost("/intents", PushAsync)
            .WithName("PushSyncIntents")
            .RequirePermission(Permission.DossierLire)
            .Produces<PushSyncIntentsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/changes", PullAsync)
            .WithName("PullSyncChanges")
            .RequirePermission(Permission.DossierLire)
            .Produces<SyncChangesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/device-series", OpenDeviceSeriesAsync)
            .WithName("OpenDeviceSeries")
            .RequirePermission(Permission.FactureValider)
            .Produces<OpenDeviceSeriesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Postes du dossier (étape 4.4) : registre, révocation, durée maximale hors ligne.
        group.MapGet("/devices", ListDevicesAsync)
            .WithName("ListSyncDevices")
            .RequirePermission(Permission.PostesGerer)
            .Produces<IReadOnlyList<SyncDeviceResponse>>();

        group.MapPost("/devices/{deviceId:guid}/revocation", RevokeDeviceAsync)
            .WithName("RevokeSyncDevice")
            .RequirePermission(Permission.PostesGerer)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/devices/{deviceId:guid}/release", ReleaseDeviceSeriesAsync)
            .WithName("ReleaseDeviceSeries")
            .RequirePermission(Permission.PostesGerer)
            .Produces<ReleaseDeviceSeriesResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/policy", GetPolicyAsync)
            .WithName("GetSyncPolicy")
            .RequirePermission(Permission.PostesGerer)
            .Produces<SyncPolicyResponse>();

        group.MapPut("/policy", SavePolicyAsync)
            .WithName("SaveSyncPolicy")
            .RequirePermission(Permission.PostesGerer)
            .Produces<SyncPolicyResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> ListDevicesAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SyncDeviceResponse>> result = await queries.QueryAsync(new ListSyncDevicesQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> RevokeDeviceAsync(Guid deviceId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result result = await commands.SendAsync(new RevokeSyncDeviceCommand(tenant.Current.DossierId, deviceId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.NoContent() : result.Errors.ToProblem();
    }

    private static async Task<IResult> ReleaseDeviceSeriesAsync(Guid deviceId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<ReleaseDeviceSeriesResponse> result = await commands.SendAsync(new ReleaseDeviceSeriesCommand(tenant.Current.DossierId, deviceId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> GetPolicyAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SyncPolicyResponse> result = await queries.QueryAsync(new GetSyncPolicyQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> SavePolicyAsync(SaveSyncPolicyRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<SyncPolicyResponse> result = await commands.SendAsync(new SaveSyncPolicyCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    /// <summary>Le poste qui appelle (en-tête <see cref="SyncHttp.DeviceHeader"/>), s'il se nomme.</summary>
    private static Guid? DeviceOf(HttpContext http) =>
        Guid.TryParse(http.Request.Headers[SyncHttp.DeviceHeader].ToString(), out Guid id) && id != Guid.Empty ? id : null;

    private static async Task<IResult> OpenDeviceSeriesAsync(OpenDeviceSeriesRequest request, SyncServer sync, CancellationToken cancellationToken)
    {
        Result<OpenDeviceSeriesResponse> result = await sync.OpenDeviceSeriesAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> PushAsync(PushSyncIntentsRequest request, SyncServer sync, CancellationToken cancellationToken)
    {
        Result<PushSyncIntentsResponse> result = await sync.PushAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> PullAsync(HttpContext http, SyncServer sync, CancellationToken cancellationToken, string? token = null, int limit = 500)
    {
        Result<SyncChangesResponse> result = await sync.PullAsync(token, limit, cancellationToken, DeviceOf(http)).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
