using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;

namespace Lettra.Api.Infrastructure;

/// <summary>Résout et vérifie le dossier avant chaque endpoint du groupe <c>/api/v1</c>.</summary>
internal sealed class TenantEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        ITenantInitializer initializer = context.HttpContext.RequestServices.GetRequiredService<ITenantInitializer>();
        Result result = await initializer.InitializeAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        return result.IsSuccess ? await next(context).ConfigureAwait(false) : result.Errors.ToProblem();
    }
}
