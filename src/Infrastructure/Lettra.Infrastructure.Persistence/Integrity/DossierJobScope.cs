using Lettra.Application.Abstractions.Tenancy;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Infrastructure.Persistence.Integrity;

/// <summary>
/// Exécute un travail de fond (Worker) dans la portée d'un dossier désigné par le catalogue, sans utilisateur : la
/// base est ouverte par la même fabrique, avec la vérification de l'estampille. Réservé aux jobs système ; les
/// requêtes des utilisateurs passent toujours par <see cref="ITenantInitializer"/> et leurs droits.
/// </summary>
public static class DossierJobScope
{
    public static async Task<T> RunAsync<T>(
        IServiceScopeFactory scopes,
        TenantDescriptor dossier,
        Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(work);
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(dossier);
            return await work(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
