using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Portal;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Exports.Application.Portal;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Espace client, côté cabinet : créer un lien pour un client (espace complet, ou signature d'un devis avec acompte), voir
/// les liens d'un client et en révoquer un, lire la preuve de signature d'un devis. Les pages vues par le client sont
/// servies hors de <c>/api/v1</c> (<c>CustomerPortalHosting</c>).
/// </summary>
internal static class PortalLinkEndpoints
{
    public static RouteGroupBuilder MapPortalLinks(this RouteGroupBuilder api)
    {
        RouteGroupBuilder links = api.MapGroup("/portal-links").WithTags("Portal");

        links.MapPost("/", async (CreatePortalLinkRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                Created(link => $"/api/v1/portal-links/{link.Id}",
                    await commands.SendAsync(new CreatePortalLinkCommand(tenant.Current.DossierId, request), ct).ConfigureAwait(false)))
            .RequirePermission(Permission.FactureEmettre)
            .WithName("CreatePortalLink")
            .Produces<PortalLinkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        links.MapGet("/", async (Guid customerId, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                Ok(await queries.QueryAsync(new ListPortalLinksQuery(tenant.Current.DossierId, customerId), ct).ConfigureAwait(false)))
            .RequirePermission(Permission.TiersConsulter)
            .WithName("ListPortalLinks")
            .Produces<IReadOnlyList<PortalLinkSummary>>();

        links.MapPost("/{linkId:guid}/revoke", async (Guid linkId, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                NoContent(await commands.SendAsync(new RevokePortalLinkCommand(tenant.Current.DossierId, linkId), ct).ConfigureAwait(false)))
            .RequirePermission(Permission.FactureEmettre)
            .WithName("RevokePortalLink")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Un devis sans signature en ligne est le cas courant : 204, pas une erreur dans la console du navigateur.
        api.MapGet("/quotes/{quoteId:guid}/signature", async (Guid quoteId, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
            {
                Result<QuoteSignatureResponse> signature = await queries.QueryAsync(new GetQuoteSignatureQuery(tenant.Current.DossierId, quoteId), ct).ConfigureAwait(false);
                return signature.IsFailure && signature.Errors[0].Code == PortalErrors.SignatureNotFoundCode ? Results.NoContent() : Ok(signature);
            })
            .RequirePermission(Permission.DossierLire)
            .WithTags("Portal")
            .WithName("GetQuoteSignature")
            .Produces<QuoteSignatureResponse>()
            .Produces(StatusCodes.Status204NoContent);

        return links;
    }
}
