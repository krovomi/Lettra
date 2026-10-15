using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;
using Lettra.Supervision.Domain;

namespace Lettra.Supervision.Application;

/// <summary>
/// Annuaire des rôles du routage (annexe G §6) : les personnes rattachées au chemin de la portée (dossier, ses groupes,
/// son cabinet), puis seulement celles qui détiennent la permission sur cette portée, par le même calcul que l'accès
/// (<see cref="IPermissionService"/>). Une règle ne nomme jamais une personne.
/// </summary>
public sealed class PermissionRoleDirectory(
    IAuthorizationDirectory directory,
    IScopeMemberDirectory members,
    IPermissionService permissions) : IRoleDirectory
{
    public async Task<IReadOnlyCollection<Recipient>> HoldersAsync(Permission permission, AlertScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.IsTechnical
            ? []
            : await HoldersAsync(permission, AlertScopes.ToAuthorization(scope), cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyCollection<Recipient>> CabinetAdminsAsync(Guid cabinet, CancellationToken cancellationToken) =>
        HoldersAsync(Permission.CabinetAdministrer, AuthorizationScope.ForCabinet(new OrganisationId(cabinet)), cancellationToken);

    private async Task<IReadOnlyCollection<Recipient>> HoldersAsync(Permission permission, AuthorizationScope scope, CancellationToken cancellationToken)
    {
        ScopePath? path = await directory.GetScopePathAsync(scope, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return [];
        }

        List<Recipient> holders = [];
        foreach (Guid user in await members.ListCandidatesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            IReadOnlySet<Permission> granted = await permissions.GetEffectiveAsync(user, scope, cancellationToken).ConfigureAwait(false);
            if (granted.Contains(permission))
            {
                holders.Add(new Recipient(RecipientKind.User, user.ToString()));
            }
        }

        return holders;
    }
}
