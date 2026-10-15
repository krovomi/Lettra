using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Client;

/// <summary>
/// Préférence de synchronisation gardée dans la réplique du dossier sur le poste : elle survit au redémarrage de
/// l'application, et reste celle du poste (le serveur ne la reçoit pas). Sans réglage, on demande à chaque fois.
/// </summary>
internal sealed class ReplicaSyncPreferences(IServiceScopeFactory scopes) : ISyncPreferences
{
    internal const string Key = "client.preference";

    public async Task<SyncPreference> GetAsync(CancellationToken cancellationToken)
    {
        string? stored = await InDossierAsync(r => r.GetSettingAsync(Key, cancellationToken), cancellationToken).ConfigureAwait(false);
        return Enum.TryParse(stored, out SyncPreference preference) && Enum.IsDefined(preference) ? preference : SyncPreference.AlwaysAsk;
    }

    public Task SetAsync(SyncPreference preference, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference), preference, "Préférence de synchronisation inconnue.");
        }

        return SaveAsync(preference, cancellationToken);
    }

    private async Task SaveAsync(SyncPreference preference, CancellationToken cancellationToken)
    {
        bool saved = await InDossierAsync(
            async r =>
            {
                await r.SaveSettingAsync(Key, preference.ToString(), cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        if (!saved)
        {
            throw new InvalidOperationException("Aucun dossier ouvert sur le poste : la préférence ne peut pas être gardée.");
        }
    }

    private async Task<T?> InDossierAsync<T>(Func<ISyncReplica, Task<T>> work, CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if ((await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
            {
                // Dossier fermé : la préférence par défaut vaut, et un réglage ne peut pas être gardé.
                return default;
            }

            return await work(scope.ServiceProvider.GetRequiredService<ISyncReplica>()).ConfigureAwait(false);
        }
    }
}
