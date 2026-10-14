using Lettra.Application.Abstractions.Identity;

namespace Lettra.Application.Authorization;

/// <summary>
/// Permissions effectives lues dans le registre (<see cref="IAuthorizationDirectory"/>) à chaque appel.
/// Côté serveur, un cache court invalidé par <c>DroitsModifies</c> l'enveloppe (Infrastructure.Identity.Oidc).
/// </summary>
public sealed class DirectoryPermissionService(IAuthorizationDirectory directory, TimeProvider clock) : IPermissionService
{
    public async ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken)
    {
        UserAuthorizationProfile? profile = await directory.FindUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is not { State: AccountState.Active })
        {
            return PermissionResolver.None;
        }

        ScopePath? path = await directory.GetScopePathAsync(scope, cancellationToken).ConfigureAwait(false);
        return PermissionResolver.Resolve(profile, path, clock.GetUtcNow());
    }
}
