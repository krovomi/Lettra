namespace Lettra.UI.Abstractions;

/// <summary>
/// Coffre du poste où l'hôte garde le code PIN, haché (étape 4.4) : MAUI le met dans le coffre de l'OS
/// (SecureStorage). Sans coffre (navigateur), l'application ne propose pas de code PIN.
/// </summary>
public interface IDeviceLockStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Remplace la valeur gardée ; <c>null</c> l'efface.</summary>
    Task WriteAsync(string? value, CancellationToken cancellationToken);
}
