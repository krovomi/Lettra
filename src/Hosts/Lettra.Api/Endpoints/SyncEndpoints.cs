using System.Diagnostics;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Diagnostics;
using Lettra.Application.Features.Sync;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Sync.Server;
using static Lettra.Api.Infrastructure.HttpResults;

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
            .AddEndpointFilter(Measure("push"))
            .WithName("PushSyncIntents")
            .RequirePermission(Permission.DossierLire)
            .Produces<PushSyncIntentsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/changes", PullAsync)
            .AddEndpointFilter(Measure("pull"))
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

    /// <summary>
    /// Durée d'un échange de synchronisation (<c>lettra.sync.duration</c>), par sens et par issue : réussi si la
    /// réponse est un succès HTTP. Rien du poste ni du dossier dans les étiquettes.
    /// </summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Measure(string direction) =>
        async (context, next) =>
        {
            long started = Stopwatch.GetTimestamp();
            string outcome = LettraTelemetry.Outcomes.Error;
            try
            {
                object? result = await next(context).ConfigureAwait(false);
                outcome = result is IStatusCodeHttpResult { StatusCode: >= 400 } ? LettraTelemetry.Outcomes.Failure : LettraTelemetry.Outcomes.Success;
                return result;
            }
            finally
            {
                LettraTelemetry.SyncDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new TagList
                {
                    { LettraTelemetry.Attributes.SyncDirection, direction },
                    { LettraTelemetry.Attributes.Outcome, outcome },
                });
            }
        };

    private static async Task<IResult> ListDevicesAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SyncDeviceResponse>> result = await queries.QueryAsync(new ListSyncDevicesQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> RevokeDeviceAsync(Guid deviceId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result result = await commands.SendAsync(new RevokeSyncDeviceCommand(tenant.Current.DossierId, deviceId), cancellationToken).ConfigureAwait(false);
        return NoContent(result);
    }

    private static async Task<IResult> ReleaseDeviceSeriesAsync(Guid deviceId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<ReleaseDeviceSeriesResponse> result = await commands.SendAsync(new ReleaseDeviceSeriesCommand(tenant.Current.DossierId, deviceId), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> GetPolicyAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<SyncPolicyResponse> result = await queries.QueryAsync(new GetSyncPolicyQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> SavePolicyAsync(SaveSyncPolicyRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<SyncPolicyResponse> result = await commands.SendAsync(new SaveSyncPolicyCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Le poste qui appelle (en-tête <see cref="SyncHttp.DeviceHeader"/>), s'il se nomme.</summary>
    private static Guid? DeviceOf(HttpContext http) =>
        Guid.TryParse(http.Request.Headers[SyncHttp.DeviceHeader].ToString(), out Guid id) && id != Guid.Empty ? id : null;

    private static async Task<IResult> OpenDeviceSeriesAsync(OpenDeviceSeriesRequest request, SyncServer sync, CancellationToken cancellationToken)
    {
        Result<OpenDeviceSeriesResponse> result = await sync.OpenDeviceSeriesAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> PushAsync(PushSyncIntentsRequest request, SyncServer sync, CancellationToken cancellationToken)
    {
        Result<PushSyncIntentsResponse> result = await sync.PushAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> PullAsync(HttpContext http, SyncServer sync, CancellationToken cancellationToken, string? token = null, int limit = 500)
    {
        Result<SyncChangesResponse> result = await sync.PullAsync(token, limit, cancellationToken, DeviceOf(http)).ConfigureAwait(false);
        return Ok(result);
    }
}
