using Lettra.Api.Security;
using Lettra.Contracts.Presence;
using Lettra.Contracts.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Api.Presence;

/// <summary>Hub de présence : même authentification que l'API (jeton porteur ou cookie du BFF en SameSite=Strict).</summary>
public static class PresenceRegistration
{
    public static IServiceCollection AddLettraPresence(this IServiceCollection services)
    {
        services.AddSignalR(o => o.ClientTimeoutInterval = TimeSpan.FromSeconds(PresenceHubContract.HeartbeatSeconds * 2))
            .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
        return services;
    }

    public static IEndpointConventionBuilder MapLettraPresence(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHub<PresenceHub>(PresenceHubContract.Path).RequireAuthorization(LettraPolicies.User);
}
