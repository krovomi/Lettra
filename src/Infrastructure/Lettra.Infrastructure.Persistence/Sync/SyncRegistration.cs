using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Persistence.Sync;

/// <summary>Portée enfant installée directement sur le dossier déjà résolu (et autorisé) de la portée parente.</summary>
internal sealed class IsolatedDossierScope(IServiceScopeFactory scopes, ITenantContext tenant) : IDossierWorkScope
{
    public async Task<T> RunAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        TenantDescriptor current = tenant.Current;
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(current);
            return await work(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}

public static class SyncRegistration
{
    /// <summary>Outbox, réplique et réponses gardées : tables de toute base de dossier, utilisées selon le rôle (poste ou serveur).</summary>
    internal static IServiceCollection AddSyncStores(this IServiceCollection services)
    {
        services.TryAddScoped<SyncOutboxStaging>();
        services.AddScoped<ISyncOutbox, EfSyncOutbox>();
        services.AddScoped<ISyncReplica, EfSyncReplica>();
        services.AddScoped<ISyncReceipts, EfSyncReceipts>();
        services.AddScoped<ISyncChangeFeed, EfSyncChangeFeed>();
        services.AddScoped<IDeviceSeriesRegistry, EfDeviceSeriesRegistry>();
        services.AddScoped<ISyncDeviceRegistry, EfSyncDeviceRegistry>();
        services.AddScoped<IDossierWorkScope, IsolatedDossierScope>();
        return services;
    }

    /// <summary>
    /// Côté serveur seulement : tenir le journal de synchronisation à chaque enregistrement d'un tiers ou d'un article.
    /// Un poste ne l'active pas (il ne sert de source à personne).
    /// </summary>
    public static IServiceCollection AddSyncJournal(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDossierInterceptor, SyncJournalInterceptor>());
        return services;
    }

    /// <summary>
    /// Côté poste seulement : les pièces validées dans la série du poste partent dans l'outbox avec leur validation
    /// (ADR-0007). À appeler par la composition d'un poste qui synchronise, avec <c>AddSyncClient</c>.
    /// </summary>
    public static IServiceCollection AddSyncDevice(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDossierInterceptor, SyncFactInterceptor>());
        return services;
    }
}
