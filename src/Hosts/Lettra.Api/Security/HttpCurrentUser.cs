using System.Security.Claims;
using Lettra.Application.Abstractions.Identity;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>
/// Utilisateur de la requête HTTP, tiré des claims (jeton porteur ou session du BFF). Sans <c>sub</c> valide, ou
/// sans second facteur quand la politique l'exige, il n'est pas authentifié pour Lettra.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, IOptions<OidcOptions> options) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public bool IsAuthenticated =>
        Principal is ClaimsPrincipal principal
        && OidcClaims.UserId(principal) is not null
        && (!options.Value.RequireMfa || OidcClaims.HasStrongAuthentication(principal, options.Value));

    public Guid UserId => Principal is ClaimsPrincipal principal && IsAuthenticated ? OidcClaims.UserId(principal)!.Value : Guid.Empty;
}
