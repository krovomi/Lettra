using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Identity.Local;

public static class DependencyInjection
{
    /// <summary>Mode 1 : compte local à phrase secrète. Aucun cas d'usage protégé ne passe avant le déverrouillage.</summary>
    public static IServiceCollection AddLocalIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LocalIdentityOptions>().Bind(configuration.GetSection(LocalIdentityOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LocalAccountStore>();
        services.AddSingleton<LocalSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<LocalSession>());
        services.Replace(ServiceDescriptor.Singleton<ILocalAccounts, LocalAccounts>());
        // Le registre local des dossiers (AddLocalDossiers, appelé avant) fournit le vrai cabinet du poste.
        services.TryAddSingleton<ILocalCabinet, ConfiguredLocalCabinet>();
        services.AddSingleton<IAuthorizationDirectory, LocalAuthorizationDirectory>();
        services.AddSingleton<IScopeMemberDirectory, LocalAuthorizationDirectory>();
        services.AddLettraAuthorization();
        return services;
    }
}
