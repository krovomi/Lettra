using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Security;

/// <summary>
/// Premier contrôle, à l'entrée de l'API (annexe F §4, « vérifié deux fois ») : la permission de la route sur sa
/// portée (le dossier de la requête, ou le cabinet ou le groupe de la route). Le pipeline Application revérifie la
/// permission déclarée par le cas d'usage.
/// </summary>
internal sealed class PermissionEndpointFilter(Permission permission, Func<HttpContext, Result<AuthorizationScope>> scopeOf) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        IServiceProvider services = context.HttpContext.RequestServices;
        ICurrentUser user = services.GetRequiredService<ICurrentUser>();
        if (!user.IsAuthenticated)
        {
            return new[] { SecurityErrors.Unauthenticated }.ToProblem();
        }

        Result<AuthorizationScope> scope = scopeOf(context.HttpContext);
        if (scope.IsFailure)
        {
            return scope.Errors.ToProblem();
        }

        IReadOnlySet<Permission> granted = await services.GetRequiredService<IPermissionService>()
            .GetEffectiveAsync(user.UserId, scope.Value, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return granted.Contains(permission)
            ? await next(context).ConfigureAwait(false)
            : new[] { SecurityErrors.Forbidden }.ToProblem();
    }

    /// <summary>Le dossier de la requête (en-tête <c>X-Dossier</c>), résolu avant le filtre.</summary>
    public static Result<AuthorizationScope> DossierOf(HttpContext http)
    {
        ITenantContext tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        return tenant.IsResolved ? AuthorizationScope.ForDossier(tenant.Current.DossierId) : TenancyErrors.DossierRequired;
    }

    /// <summary>L'identifiant de la route <paramref name="key"/> ; une valeur illisible est refusée comme un accès interdit.</summary>
    public static Result<AuthorizationScope> RouteScope(HttpContext http, string key, Func<Guid, AuthorizationScope> scope) =>
        Guid.TryParse(http.GetRouteValue(key) as string, out Guid id) ? scope(id) : SecurityErrors.Forbidden;
}

/// <summary>Routes d'administration du cabinet (<c>/api/v1/cabinets/{cabinetId}/…</c>) : la permission porte sur le cabinet, sans dossier.</summary>
internal static class CabinetPermissionEndpointFilter
{
    public const string RouteKey = "cabinetId";

    public static Result<AuthorizationScope> ScopeOf(HttpContext http) =>
        PermissionEndpointFilter.RouteScope(http, RouteKey, id => AuthorizationScope.ForCabinet(new OrganisationId(id)));
}

/// <summary>Routes d'un groupe (<c>/api/v1/groupes/{groupId}/…</c>) : la permission porte sur le groupe.</summary>
internal static class GroupPermissionEndpointFilter
{
    public const string RouteKey = "groupId";

    public static Result<AuthorizationScope> ScopeOf(HttpContext http) =>
        PermissionEndpointFilter.RouteScope(http, RouteKey, id => AuthorizationScope.ForGroup(new GroupId(id)));
}

/// <summary>Permission d'une route, lisible par les tests (toute route de l'API en déclare une).</summary>
public sealed record RequiredPermissionMetadata(Permission Permission);

public static class PermissionEndpointExtensions
{
    /// <summary>Exige <paramref name="permission"/> sur le dossier de la requête avant d'appeler le cas d'usage.</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, Permission permission) =>
        builder.Require(permission, PermissionEndpointFilter.DossierOf);

    /// <summary>Exige <paramref name="permission"/> sur le cabinet de la route avant d'appeler le cas d'usage.</summary>
    public static RouteHandlerBuilder RequireCabinetPermission(this RouteHandlerBuilder builder, Permission permission) =>
        builder.Require(permission, CabinetPermissionEndpointFilter.ScopeOf);

    /// <summary>Exige <paramref name="permission"/> sur le groupe de la route avant d'appeler le cas d'usage.</summary>
    public static RouteHandlerBuilder RequireGroupPermission(this RouteHandlerBuilder builder, Permission permission) =>
        builder.Require(permission, GroupPermissionEndpointFilter.ScopeOf);

    /// <summary>
    /// Route qui ne rend que les éléments où l'utilisateur a <paramref name="permission"/> (liste des dossiers
    /// accessibles) : l'authentification suffit à l'entrée, le cas d'usage filtre.
    /// </summary>
    public static RouteHandlerBuilder FilterByPermission(this RouteHandlerBuilder builder, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new RequiredPermissionMetadata(permission));
    }

    private static RouteHandlerBuilder Require(this RouteHandlerBuilder builder, Permission permission, Func<HttpContext, Result<AuthorizationScope>> scopeOf)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission, scopeOf))
            .WithMetadata(new RequiredPermissionMetadata(permission));
    }
}

/// <summary>
/// Route personnelle (<c>/api/v1/me/…</c>), hors de tout dossier : aucune permission à vérifier, le cas d'usage
/// (<c>[PublicUseCase]</c>) ne lit et n'écrit que le compte de la session. Seule exception admise à la permission
/// déclarée (annexe F §4), et seulement sous <c>/api/v1/me/</c>.
/// </summary>
public sealed record OwnAccountOnlyMetadata;

public static class OwnAccountEndpointExtensions
{
    /// <summary>Déclare les routes du groupe comme personnelles : l'authentification suffit, l'identité vient de la session.</summary>
    public static RouteGroupBuilder ForOwnAccountOnly(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new OwnAccountOnlyMetadata());
    }
}
