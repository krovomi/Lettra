using Lettra.Contracts.Common;
using Lettra.Contracts.Preferences;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Les préférences rangées dans le compte de l'utilisateur connecté. Sans base Catalog (poste isolé) ou face à un
/// serveur qui ne connaît pas encore cette route, chaque appel rend <c>Preferences.Unavailable</c> : l'interface garde
/// alors ses préférences sur l'appareil.
/// </summary>
public interface IUserPreferencesGateway
{
    Task<OperationResult<UserPreferencesResponse>> GetAsync(CancellationToken cancellationToken);

    Task<OperationResult<UserPreferencesResponse>> SaveAsync(SaveUserPreferencesRequest request, CancellationToken cancellationToken);

    Task<OperationResult<UserPreferencesResponse>> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken);

    /// <summary>Oublie les choix comptés d'un dossier, ou de tous quand <paramref name="dossierId"/> est <c>null</c>.</summary>
    Task<OperationResult<UserPreferencesResponse>> ForgetUsageAsync(Guid? dossierId, CancellationToken cancellationToken);

    Task<OperationResult<UserPreferencesResponse>> ResetAsync(CancellationToken cancellationToken);
}
