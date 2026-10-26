using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Fiche anti-blanchiment (LCB-FT) du client du dossier ouvert (en-tête <c>X-Dossier</c>).</summary>
internal static class VigilanceEndpoints
{
    public static RouteGroupBuilder MapVigilance(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/vigilance").WithTags("Cabinet");

        group.MapGet("/", GetAsync)
            .RequirePermission(Permission.VigilanceConsulter)
            .WithName("GetVigilance")
            .Produces<VigilanceOverviewResponse>();

        group.MapPut("/", SaveAsync)
            .RequirePermission(Permission.VigilanceGerer)
            .WithName("SaveVigilance")
            .Produces<VigilanceOverviewResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireIfMatch();
        return group;
    }

    private static async Task<IResult> GetAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new GetVigilanceQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SaveAsync(
        SaveDueDiligenceRequest request, HttpContext http, ITenantContext tenant, ICommandDispatcher commands, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DossierId dossier = tenant.Current.DossierId;
        Result saved = await commands.SendAsync(new SaveVigilanceCommand(dossier, request.ToDraft(), http.IfMatchVersion()), cancellationToken).ConfigureAwait(false);
        Task<Result<VigilanceOverviewResponse>> Read() => queries.QueryAsync(new GetVigilanceQuery(dossier), cancellationToken);
        return saved.IsFailure
            ? await ConcurrencyResults.WriteFailureAsync(saved.Errors, Read, o => o.File?.Version ?? 0).ConfigureAwait(false)
            : Versioned(await Read().ConfigureAwait(false), o => o.File?.Version ?? 0);
    }
}
