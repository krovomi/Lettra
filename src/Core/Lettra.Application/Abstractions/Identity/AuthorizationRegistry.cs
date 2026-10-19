using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>Une personne du registre : son nom et son adresse servent à l'administrateur, jamais au calcul des droits.</summary>
public sealed record RegistryPerson(Guid Id, string? DisplayName, string? Email, AccountState State, DateTimeOffset CreatedAt);

public sealed record RegistryGroup(Guid Id, string Name);

public sealed record RegistryMembership(Guid GroupId, Guid UserId);

public sealed record RegistryPlacement(Guid GroupId, Guid DossierId);

/// <summary>Une attribution enregistrée, avec son identifiant (pour la retirer).</summary>
public sealed record RegistryAssignment(Guid Id, RoleAssignment Assignment);

/// <summary>Un rôle sur mesure du cabinet, avec le nom et la phrase que l'administrateur lui a donnés.</summary>
public sealed record RegistryCustomRole(RoleDefinition Definition, string? Name, string? Description);

/// <summary>
/// Tout le registre d'un cabinet, lu d'un coup pour l'espace d'administration. <paramref name="ReadOnly"/> : le registre
/// ne s'administre pas sur cet hôte (poste isolé, dont le seul compte est le propriétaire).
/// </summary>
public sealed record CabinetRegistry(
    OrganisationId Cabinet,
    string? CabinetName,
    bool ReadOnly,
    IReadOnlyList<RegistryPerson> People,
    IReadOnlyList<RegistryGroup> Groups,
    IReadOnlyList<RegistryMembership> Memberships,
    IReadOnlyList<RegistryPlacement> Placements,
    IReadOnlyList<RegistryAssignment> Assignments,
    IReadOnlyList<RegistryCustomRole> CustomRoles);

/// <summary>
/// Lecture du registre des droits d'un cabinet et écritures propres à l'espace d'administration (personnes, équipes,
/// rôles sur mesure). Les écritures historiques (états, membres, attributions) restent sur
/// <see cref="IAuthorizationAdministration"/>. Base Catalog en modes 2, 3 et 5 ; lecture seule sur le poste isolé.
/// Comme pour l'administration, tout ce qui est lu ou écrit doit appartenir au cabinet donné.
/// </summary>
public interface IAuthorizationRegistry
{
    /// <summary><c>null</c> si cet hôte n'a pas de registre (registre en configuration).</summary>
    Task<CabinetRegistry?> ReadAsync(OrganisationId cabinet, CancellationToken cancellationToken);

    /// <summary>Ajoute une personne « invitée » ; une adresse déjà prise dans le cabinet est refusée.</summary>
    Task<Result<Guid>> InviteAsync(OrganisationId cabinet, string displayName, string email, CancellationToken cancellationToken);

    Task<Result> UpdatePersonAsync(OrganisationId cabinet, Guid userId, string displayName, string email, CancellationToken cancellationToken);

    Task<Result> RenameGroupAsync(OrganisationId cabinet, GroupId group, string name, CancellationToken cancellationToken);

    /// <summary>Supprime l'équipe, ses appartenances, ses placements de dossiers et ses attributions.</summary>
    Task<Result> DeleteGroupAsync(OrganisationId cabinet, GroupId group, CancellationToken cancellationToken);

    /// <summary>Crée ou remplace le rôle sur mesure (même clé) et toutes ses permissions.</summary>
    Task<Result> SaveCustomRoleAsync(OrganisationId cabinet, RegistryCustomRole role, CancellationToken cancellationToken);

    /// <summary>Supprime un rôle sur mesure qui n'est plus attribué à personne.</summary>
    Task<Result> DeleteCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken);
}

public static class AdministrationErrors
{
    public static Error NameRequired => Error.Validation(ErrorCodes.Administration.NameRequired, "DisplayName");

    public static Error NameTooLong => Error.Validation(ErrorCodes.Administration.NameTooLong, "DisplayName");

    public static Error EmailInvalid => Error.Validation(ErrorCodes.Administration.EmailInvalid, "Email");

    public static Error EmailTaken => Error.Validation(ErrorCodes.Administration.EmailTaken, "Email");

    public static Error RoleNameRequired => Error.Validation(ErrorCodes.Administration.RoleNameRequired, "Name");

    public static Error RoleNameTooLong => Error.Validation(ErrorCodes.Administration.RoleNameTooLong, "Name");

    public static Error RoleDescriptionTooLong => Error.Validation(ErrorCodes.Administration.RoleDescriptionTooLong, "Description");

    public static Error RoleEmpty => Error.Validation(ErrorCodes.Administration.RoleEmpty, "Areas");

    public static Error RoleAreaInvalid => Error.Validation(ErrorCodes.Administration.RoleAreaInvalid, "Areas");

    public static Error RoleNotFound => Error.NotFound(ErrorCodes.Administration.RoleNotFound);

    public static Error RoleInUse => new(ErrorCodes.Administration.RoleInUse, ErrorKind.Conflict);

    /// <summary>Un administrateur ne se retire pas lui-même l'accès : il faut un autre administrateur.</summary>
    public static Error SelfLockout => new(ErrorCodes.Administration.SelfLockout, ErrorKind.Conflict);

    /// <summary>La personne n'est plus « invitée » : rien à renvoyer.</summary>
    public static Error NotInvited => new(ErrorCodes.Administration.NotInvited, ErrorKind.Conflict);
}

/// <summary>Registre en configuration : rien à lire ni à écrire.</summary>
internal sealed class NoAuthorizationRegistry : IAuthorizationRegistry
{
    public Task<CabinetRegistry?> ReadAsync(OrganisationId cabinet, CancellationToken cancellationToken) => Task.FromResult<CabinetRegistry?>(null);

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
}
