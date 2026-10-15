using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
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

        return group;
    }

    private static async Task<IResult> PushAsync(PushSyncIntentsRequest request, SyncServer sync, CancellationToken cancellationToken)
    {
        Result<PushSyncIntentsResponse> result = await sync.PushAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> PullAsync(SyncServer sync, CancellationToken cancellationToken, string? token = null, int limit = 500)
    {
        Result<SyncChangesResponse> result = await sync.PullAsync(token, limit, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }
}
