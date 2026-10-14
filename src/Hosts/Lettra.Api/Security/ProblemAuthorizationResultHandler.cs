using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Lettra.Api.Security;

/// <summary>
/// Les refus d'authentification de l'API sont des réponses RFC 9457 avec un code, comme les autres erreurs ;
/// jamais une redirection vers la page de connexion (c'est le rôle de <c>/bff/login</c>).
/// </summary>
internal sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        bool mfaMissing = authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Message == nameof(MfaRequirement)) == true;
        Domain.Common.Error error = context.User.Identity?.IsAuthenticated != true
            ? SecurityErrors.Unauthenticated
            : mfaMissing ? SecurityErrors.MfaRequired : SecurityErrors.Forbidden;

        if (error == SecurityErrors.Unauthenticated || error == SecurityErrors.MfaRequired)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        await new[] { error }.ToProblem().ExecuteAsync(context).ConfigureAwait(false);
    }
}
