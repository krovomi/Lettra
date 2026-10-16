using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>
/// Registre du poste isolé : un seul utilisateur, propriétaire du poste, manager et administrateur du cabinet du
/// poste (<see cref="ILocalCabinet"/>). Les droits passent quand même par le même calcul que le serveur
/// (<see cref="PermissionResolver"/>) : un dossier ou un cabinet étranger au poste reste fermé.
/// </summary>
internal sealed class LocalAuthorizationDirectory(
    LocalAccountStore store,
    ITenantCatalog catalog,
    ILocalCabinet localCabinet) : IAuthorizationDirectory, IScopeMemberDirectory
{
    public async ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (account is null || account.UserId != userId)
        {
            return null;
        }

        OrganisationId local = await localCabinet.GetAsync(cancellationToken).ConfigureAwait(false);
        SubjectRef owner = new(SubjectKind.User, userId);
        AuthorizationScope cabinet = AuthorizationScope.ForCabinet(local);
        return new UserAuthorizationProfile(
            userId,
            AccountState.Active,
            local,
            Groups: [],
            Assignments:
            [
                new RoleAssignment(owner, RoleTemplates.Manager, cabinet),
                new RoleAssignment(owner, RoleTemplates.AdministrateurCabinet, cabinet),
            ],
            CustomRoles: []);
    }

    public async ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken)
    {
        switch (scope.Kind)
        {
            case ScopeKind.Dossier:
                // Le cabinet d'un dossier est celui de son manifeste : le calcul refuse un dossier rattaché ailleurs.
                TenantDescriptor? dossier = await catalog.FindAsync(new DossierId(scope.Id), cancellationToken).ConfigureAwait(false);
                return dossier is null
                    ? null
                    : new ScopePath(scope, dossier.OrganisationId, [scope, AuthorizationScope.ForCabinet(dossier.OrganisationId), AuthorizationScope.Plateforme]);
            case ScopeKind.Cabinet:
                OrganisationId local = await localCabinet.GetAsync(cancellationToken).ConfigureAwait(false);
                return scope == AuthorizationScope.ForCabinet(local) ? new ScopePath(scope, local, [scope, AuthorizationScope.Plateforme]) : null;
            default:
                return null;
        }
    }

    /// <summary>Le propriétaire du poste est la seule personne concernée par une portée de son cabinet local.</summary>
    public async ValueTask<IReadOnlyCollection<Guid>> ListCandidatesAsync(ScopePath path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        OrganisationId local = await localCabinet.GetAsync(cancellationToken).ConfigureAwait(false);
        return account is not null && path.Cabinet == local ? [account.UserId] : [];
    }
}
