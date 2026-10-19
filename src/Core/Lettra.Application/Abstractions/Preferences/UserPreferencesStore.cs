using Lettra.Domain.Common;
using Lettra.Domain.Preferences;

namespace Lettra.Application.Abstractions.Preferences;

/// <summary>
/// Préférences des utilisateurs, une ligne par compte dans la base Catalog (modes 2, 3 et 5). En mode 1 (poste isolé)
/// il n'y a pas de catalogue : <see cref="IsAvailable"/> est faux et l'interface garde ses préférences sur l'appareil.
/// </summary>
public interface IUserPreferencesStore
{
    bool IsAvailable { get; }

    ValueTask<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Écrit à condition que la ligne soit toujours à la version lue (<see cref="UserPreferences.Version"/>, 0 : pas
    /// encore de ligne) ; sinon <c>Concurrency.VersionMismatch</c>. Rend les préférences à leur nouvelle version.
    /// </summary>
    ValueTask<Result<UserPreferences>> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken);
}

/// <summary>Sans base Catalog : rien n'est rangé dans le compte.</summary>
internal sealed class NoUserPreferencesStore : IUserPreferencesStore
{
    public bool IsAvailable => false;

    public ValueTask<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken) => ValueTask.FromResult<UserPreferences?>(null);

    public ValueTask<Result<UserPreferences>> SaveAsync(UserPreferences preferences, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<UserPreferences>>(UserPreferencesErrors.Unavailable);
}
