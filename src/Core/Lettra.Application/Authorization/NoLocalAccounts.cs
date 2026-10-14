using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.Common;

namespace Lettra.Application.Authorization;

/// <summary>Modes serveur : il n'y a pas de compte local, l'identité vient de Keycloak.</summary>
internal sealed class NoLocalAccounts : ILocalAccounts
{
    public ValueTask<LocalAccountStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new LocalAccountStatus(Exists: false, IsUnlocked: false, DisplayName: null));

    public ValueTask<Result> CreateAsync(string displayName, string passphrase, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result>(LocalAccountErrors.NotSupported);

    public ValueTask<Result> UnlockAsync(string passphrase, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result>(LocalAccountErrors.NotSupported);

    public void Lock()
    {
    }
}
