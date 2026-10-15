using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;

namespace Lettra.UI.Abstractions;

/// <summary>
/// Connexion interactive de l'application de bureau reliée à un serveur : la page de connexion du cabinet s'ouvre dans
/// le navigateur système (PKCE), puis l'application reprend la main. Absent dans le navigateur (BFF) et sur le poste isolé.
/// </summary>
public interface IInteractiveSignIn
{
    Task<OperationResult<SessionResponse>> SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}
