using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>Second facteur prouvé par le jeton (<c>amr</c> ou <c>acr</c>), sauf si la politique ne l'exige pas.</summary>
internal sealed class MfaRequirement : IAuthorizationRequirement;

internal sealed class MfaRequirementHandler(IOptions<OidcOptions> options) : AuthorizationHandler<MfaRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!options.Value.RequireMfa || OidcClaims.HasStrongAuthentication(context.User, options.Value))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, nameof(MfaRequirement)));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Le jeton doit désigner un utilisateur Lettra (<c>sub</c> au format UUID).</summary>
internal sealed class LettraSubjectRequirement : AuthorizationHandler<LettraSubjectRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LettraSubjectRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (OidcClaims.UserId(context.User) is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
