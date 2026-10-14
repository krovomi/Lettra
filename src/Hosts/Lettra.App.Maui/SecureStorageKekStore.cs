using System.Security.Cryptography;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Security;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Clé maîtresse (KEK) du cabinet du poste dans le coffre de l'OS : DPAPI sous Windows, Trousseau sous macOS et iOS,
/// Keystore sous Android (MAUI <see cref="SecureStorage"/>). Elle ne quitte jamais le coffre en clair sur le disque.
/// </summary>
internal sealed class SecureStorageKekStore : IKekStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<byte[]> GetOrCreateAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await FindAsync(organisation, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return existing;
            }

            byte[] kek = RandomNumberGenerator.GetBytes(32);
            await SecureStorage.Default.SetAsync(Key(organisation), Convert.ToBase64String(kek)).ConfigureAwait(false);
            return kek;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<byte[]?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken)
    {
        string? stored = await SecureStorage.Default.GetAsync(Key(organisation)).ConfigureAwait(false);
        return stored is null ? null : Convert.FromBase64String(stored);
    }

    private static string Key(OrganisationId organisation) => $"lettra.kek.{organisation.Value:N}";
}
