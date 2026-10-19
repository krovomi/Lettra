using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Dossiers;

/// <summary>
/// Les dossiers du cabinet de l'utilisateur courant où il détient une permission. Le profil est lu une fois et les
/// dossiers situés d'un coup, puis chacun passe par le même calcul que le pipeline. Partagé par le sélecteur de dossier
/// et la vue cabinet.
/// </summary>
internal sealed class AccessibleDossiers(
    ICurrentUser user,
    IAuthorizationDirectory directory,
    IDossierDirectory dossiers,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<DossierSummary>> ListAsync(Permission permission, CancellationToken cancellationToken)
    {
        UserAuthorizationProfile? profile = await directory.FindUserAsync(user.UserId, cancellationToken).ConfigureAwait(false);
        if (profile is not { State: AccountState.Active, Cabinet: OrganisationId cabinet })
        {
            return [];
        }

        IReadOnlyList<DossierSummary> all = await dossiers.ListAsync(cabinet, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ScopePath> paths = await directory
            .GetDossierPathsAsync(cabinet, all.Select(d => new DossierId(d.Id)).ToArray(), cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = clock.GetUtcNow();
        HashSet<Guid> granted = paths
            .Where(p => PermissionResolver.Resolve(profile, p, now).Contains(permission))
            .Select(p => p.Target.Id)
            .ToHashSet();
        return all.Where(d => granted.Contains(d.Id)).ToArray();
    }
}
