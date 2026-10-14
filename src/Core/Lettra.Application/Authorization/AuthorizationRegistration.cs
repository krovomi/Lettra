using Lettra.Application.Abstractions.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Application.Authorization;

public static class AuthorizationRegistration
{
    /// <summary>
    /// Calcul des permissions à partir du registre. L'hôte fournit <see cref="IAuthorizationDirectory"/> et
    /// <see cref="ICurrentUser"/> ; un adapter serveur peut envelopper le service d'un cache.
    /// </summary>
    public static IServiceCollection AddLettraAuthorization(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<DirectoryPermissionService>();
        services.TryAddScoped<IPermissionService>(sp => sp.GetRequiredService<DirectoryPermissionService>());
        return services;
    }
}
