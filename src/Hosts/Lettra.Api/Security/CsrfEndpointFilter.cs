using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Identity;

namespace Lettra.Api.Security;

/// <summary>
/// Un appel authentifié par le cookie du BFF doit porter <c>X-Lettra-Csrf: 1</c>. Un site tiers ne peut pas
/// ajouter cet en-tête sans requête préalable CORS, que l'API refuse : c'est la protection CSRF, en plus de
/// <c>SameSite=Strict</c>. Les appels par jeton porteur n'en ont pas besoin.
/// </summary>
internal sealed class CsrfEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        HttpRequest request = context.HttpContext.Request;
        bool bearer = request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
        if (!bearer && request.Headers[LettraAuthSchemes.CsrfHeader] != "1")
        {
            return new[] { SecurityErrors.CsrfHeaderMissing }.ToProblem();
        }

        return await next(context).ConfigureAwait(false);
    }
}
