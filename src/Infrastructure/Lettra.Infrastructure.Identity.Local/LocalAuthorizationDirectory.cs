using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>
/// Registre du poste isolé : un seul utilisateur, propriétaire du poste, manager et administrateur de son cabinet
/// local. Les droits passent quand même par le même calcul que le serveur (<see cref="PermissionResolver"/>).
/// </summary>
internal sealed class LocalAuthorizationDirectory(
    LocalAccountStore store,
    ITenantCatalog catalog,
    IOptions<LocalIdentityOptions> options) : IAuthorizationDirectory
{
    private OrganisationId Cabinet => new(options.Value.CabinetId);

    public async ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (account is null || account.UserId != userId)
        {
            return null;
        }

        SubjectRef owner = new(SubjectKind.User, userId);
        AuthorizationScope cabinet = AuthorizationScope.ForCabinet(Cabinet);
        return new UserAuthorizationProfile(
            userId,
            AccountState.Active,
            Cabinet,
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
        AuthorizationScope cabinet = AuthorizationScope.ForCabinet(Cabinet);
        switch (scope.Kind)
        {
            case ScopeKind.Dossier:
                TenantDescriptor? dossier = await catalog.FindAsync(new DossierId(scope.Id), cancellationToken).ConfigureAwait(false);
                return dossier is null ? null : new ScopePath(scope, Cabinet, [scope, cabinet, AuthorizationScope.Plateforme]);
            case ScopeKind.Cabinet when scope == cabinet:
                return new ScopePath(scope, Cabinet, [scope, AuthorizationScope.Plateforme]);
            default:
                return null;
        }
    }
}
