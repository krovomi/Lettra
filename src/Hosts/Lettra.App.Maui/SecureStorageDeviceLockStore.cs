using Lettra.UI.Abstractions;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Code PIN du poste (haché par <c>DeviceLock</c>) dans le coffre de l'OS, comme la clé du cabinet
/// (<see cref="SecureStorageKekStore"/>) : DPAPI, Trousseau ou Keystore selon la plateforme.
/// </summary>
internal sealed class SecureStorageDeviceLockStore : IDeviceLockStore
{
    private const string Key = "lettra.device-lock";

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => SecureStorage.Default.GetAsync(Key);

    public Task WriteAsync(string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            SecureStorage.Default.Remove(Key);
            return Task.CompletedTask;
        }

        return SecureStorage.Default.SetAsync(Key, value);
    }
}
