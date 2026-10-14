using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;

namespace Lettra.Api.Security;

/// <summary>
/// Premier contrôle, à l'entrée de l'API (annexe F §4, « vérifié deux fois ») : la permission de la route sur le
/// dossier de la requête. Le pipeline Application revérifie la permission déclarée par le cas d'usage.
/// </summary>
internal sealed class PermissionEndpointFilter(Permission permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        IServiceProvider services = context.HttpContext.RequestServices;
        ICurrentUser user = services.GetRequiredService<ICurrentUser>();
        ITenantContext tenant = services.GetRequiredService<ITenantContext>();
        if (!user.IsAuthenticated)
        {
            return new[] { SecurityErrors.Unauthenticated }.ToProblem();
        }

        if (!tenant.IsResolved)
        {
            return new[] { TenancyErrors.DossierRequired }.ToProblem();
        }

        IReadOnlySet<Permission> granted = await services.GetRequiredService<IPermissionService>()
            .GetEffectiveAsync(user.UserId, tenant.Current.DossierId, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return granted.Contains(permission)
            ? await next(context).ConfigureAwait(false)
            : new[] { SecurityErrors.Forbidden }.ToProblem();
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Exige <paramref name="permission"/> sur le dossier de la requête avant d'appeler le cas d'usage.</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission))
            .WithMetadata(new RequiredPermissionMetadata(permission));
    }
}

/// <summary>Permission d'une route, lisible par les tests (toute route de l'API en déclare une).</summary>
public sealed record RequiredPermissionMetadata(Permission Permission);
