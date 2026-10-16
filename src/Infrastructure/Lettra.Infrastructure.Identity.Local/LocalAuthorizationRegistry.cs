using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Authorization;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Identity.Local;

/// <summary>
/// Poste isolé : l'espace d'administration montre le registre tel qu'il est, en lecture seule. Le propriétaire du poste
/// est la seule personne, manager et administrateur du cabinet du poste ; inviter un collègue demande un serveur de cabinet.
/// </summary>
internal sealed class LocalAuthorizationRegistry(LocalAccountStore store, ILocalCabinet localCabinet) : IAuthorizationRegistry
{
    public async Task<CabinetRegistry?> ReadAsync(OrganisationId cabinet, CancellationToken cancellationToken)
    {
        OrganisationId local = await localCabinet.GetAsync(cancellationToken).ConfigureAwait(false);
        LocalAccountFile? account = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (cabinet != local || account is null)
        {
            return new CabinetRegistry(cabinet, null, ReadOnly: true, [], [], [], [], [], []);
        }

        SubjectRef owner = new(SubjectKind.User, account.UserId);
        AuthorizationScope scope = AuthorizationScope.ForCabinet(local);
        return new CabinetRegistry(
            local,
            CabinetName: null,
            ReadOnly: true,
            People: [new RegistryPerson(account.UserId, account.DisplayName, Email: null, AccountState.Active, account.CreatedAt)],
            Groups: [],
            Memberships: [],
            Placements: [],
            Assignments:
            [
                // Mêmes attributions que LocalAuthorizationDirectory, avec un identifiant stable pour l'affichage.
                new RegistryAssignment(Stable(account.UserId, 1), new RoleAssignment(owner, RoleTemplates.Manager, scope)),
                new RegistryAssignment(Stable(account.UserId, 2), new RoleAssignment(owner, RoleTemplates.AdministrateurCabinet, scope)),
            ],
            CustomRoles: []);
    }

    public Task<Result<Guid>> InviteAsync(OrganisationId cabinet, string displayName, string email, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Guid>.Failure(RightsErrors.NotSupported));

    public Task<Result> UpdatePersonAsync(OrganisationId cabinet, Guid userId, string displayName, string email, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result> RenameGroupAsync(OrganisationId cabinet, GroupId group, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result> DeleteGroupAsync(OrganisationId cabinet, GroupId group, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result> SaveCustomRoleAsync(OrganisationId cabinet, RegistryCustomRole role, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result> DeleteCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    private static Guid Stable(Guid user, byte index)
    {
        byte[] bytes = user.ToByteArray();
        bytes[^1] ^= index;
        return new Guid(bytes);
    }
}
