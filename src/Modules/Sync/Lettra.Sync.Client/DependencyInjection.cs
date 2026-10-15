using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Sync.Client;

public static class DependencyInjection
{
    /// <summary>
    /// Poste qui synchronise sa réplique avec un serveur (modes 2 à 5) : intentions écrites dans l'outbox avec chaque
    /// commande, et <see cref="SyncCoordinator"/>. L'hôte fournit le transport, la sonde réseau et, à l'étape 4.3, la
    /// demande à l'utilisateur ; la préférence est gardée dans la réplique.
    /// </summary>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, Action<SyncClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        SyncClientOptions options = new();
        configure(options);
        if (options.DeviceId == Guid.Empty)
        {
            throw new ArgumentException("Le poste doit avoir un identifiant (SyncClientOptions.DeviceId).", nameof(configure));
        }

        // Ajouté après les comportements de l'application : le plus interne, juste autour du handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SyncOutboxBehavior<,>));
        services.Replace(ServiceDescriptor.Scoped<IDeviceNumbering, ReplicaDeviceNumbering>());
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISyncConsentPrompt, AskEveryTimeLater>();
        services.TryAddSingleton<ISyncPreferences, ReplicaSyncPreferences>();
        services.AddSingleton<SyncCoordinator>();
        return services;
    }
}
