using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Vigilance;
using Lettra.Domain.Vigilance;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Vérification des clients et fournisseurs du dossier ouvert auprès des registres officiels (en-tête <c>X-Dossier</c>).</summary>
internal static class ThirdPartyCheckEndpoints
{
    public static RouteGroupBuilder MapThirdPartyChecks(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/third-party-checks").WithTags("Parties");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permission.VigilanceConsulter)
            .WithName("ListThirdPartyChecks")
            .Produces<ThirdPartyChecksResponse>();

        group.MapPost("/{kind}/{partyId:guid}", CheckAsync)
            .RequirePermission(Permission.VigilanceGerer)
            .WithName("CheckThirdParty")
            .Produces<ThirdPartyCheckRow>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return group;
    }

    private static async Task<IResult> ListAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(new ListThirdPartyChecksQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CheckAsync(
        ThirdPartyKindDto kind, Guid partyId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(new CheckThirdPartyCommand(tenant.Current.DossierId, (ThirdPartyKind)(int)kind, partyId), cancellationToken)
            .ConfigureAwait(false));
}
