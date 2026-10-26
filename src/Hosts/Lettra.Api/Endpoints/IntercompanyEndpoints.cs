using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Cabinet;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>Les comptes intra-groupe : hors de tout dossier, chaque dossier filtré par les droits de l'utilisateur.</summary>
internal static class IntercompanyEndpoints
{
    /// <summary>Sous <c>/api/v1/dossiers</c> : les couples de sociétés d'un même groupe parmi les dossiers lisibles.</summary>
    public static RouteGroupBuilder MapIntercompany(this RouteGroupBuilder dossiers)
    {
        dossiers.MapGet("/intercompany", GetAsync)
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Cabinet")
            .WithName("GetIntercompany")
            .Produces<IntercompanyResponse>();
        return dossiers;
    }

    private static async Task<IResult> GetAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<IntercompanyResponse> found = await queries.QueryAsync(new GetIntercompanyQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(found);
    }
}
