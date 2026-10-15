using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Infrastructure.Identity.Oidc.Offline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Identity.Oidc;

public static class DependencyInjection
{
    /// <summary>
    /// Identité serveur (modes 2, 3 et 5) : options Keycloak, registre des droits, cache des permissions.
    /// L'hôte HTTP ajoute l'authentification (JWT, BFF) et l'utilisateur courant tiré des claims.
    /// </summary>
    public static IServiceCollection AddOidcIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<OidcOptions>().Bind(configuration.GetSection(OidcOptions.SectionName));
        services.AddOptions<AuthorizationDirectoryOptions>().Bind(configuration.GetSection(AuthorizationDirectoryOptions.SectionName));
        services.AddOptions<RightsSnapshotOptions>().Bind(configuration.GetSection(RightsSnapshotOptions.SectionName));

        // Instantanés de droits pour le hors ligne (annexe F §4) : indisponibles tant qu'aucune clé n'est configurée.
        services.TryAddSingleton<EcdsaRightsSnapshotIssuer>();
        services.TryAddSingleton<IRightsSnapshotIssuer>(sp => sp.GetRequiredService<EcdsaRightsSnapshotIssuer>());

        // Le registre lu en configuration tient lieu de base Catalog tant qu'elle n'existe pas (TryAdd : elle le remplacera).
        services.TryAddSingleton<IAuthorizationDirectory, ConfigurationAuthorizationDirectory>();
        services.TryAddSingleton<IScopeMemberDirectory, ConfigurationAuthorizationDirectory>();
        services.AddLettraAuthorization();
        services.AddHybridCache();
        services.AddScoped<CachingPermissionService>();
        services.Replace(ServiceDescriptor.Scoped<IPermissionService>(sp => sp.GetRequiredService<CachingPermissionService>()));
        services.Replace(ServiceDescriptor.Scoped<IPermissionCacheInvalidator>(sp => sp.GetRequiredService<CachingPermissionService>()));

        // Hors requête HTTP (Worker, tâches de fond), personne n'est authentifié tant qu'un hôte ne dit pas qui agit.
        services.TryAddScoped<ICurrentUser, AnonymousUser>();
        return services;
    }

    private sealed class AnonymousUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid UserId => Guid.Empty;
    }
}
