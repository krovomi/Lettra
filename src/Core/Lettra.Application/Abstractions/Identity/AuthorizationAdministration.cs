using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>
/// Écriture du registre des droits (base Catalog en modes 2, 3 et 5). Le port vérifie l'appartenance au cabinet de
/// tout ce qu'il relie (compte, groupe, dossier, sujet) ; les règles des rôles (portée la plus large, échéance)
/// restent dans les cas d'usage. Le poste isolé n'a pas de registre à administrer.
/// </summary>
public interface IAuthorizationAdministration
{
    /// <summary>Crée le compte dans le cabinet ou change son état. Échoue si le compte appartient à un autre cabinet.</summary>
    Task<Result> SetAccountAsync(OrganisationId cabinet, Guid userId, AccountState state, CancellationToken cancellationToken);

    Task<Result<GroupId>> CreateGroupAsync(OrganisationId cabinet, string name, CancellationToken cancellationToken);

    /// <summary>Ajoute ou retire un membre (idempotent). Le compte doit appartenir au cabinet du groupe.</summary>
    Task<Result> SetGroupMemberAsync(GroupId group, Guid userId, bool member, CancellationToken cancellationToken);

    /// <summary>Place ou retire un dossier actif du groupe (idempotent). Le dossier doit appartenir au cabinet du groupe.</summary>
    Task<Result> SetDossierPlacementAsync(GroupId group, DossierId dossier, bool placed, CancellationToken cancellationToken);

    Task<RoleDefinition?> FindCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre l'attribution ; une attribution identique (sujet, rôle, portée) est mise à jour au lieu d'être
    /// dupliquée. Le sujet doit appartenir au cabinet.
    /// </summary>
    Task<Result<Guid>> AssignAsync(OrganisationId cabinet, RoleAssignment assignment, CancellationToken cancellationToken);

    /// <summary>Retire une attribution d'un sujet du cabinet et rend ce sujet, pour invalider ses droits en cache.</summary>
    Task<Result<SubjectRef>> RevokeAsync(OrganisationId cabinet, Guid assignmentId, CancellationToken cancellationToken);
}

public static class RightsErrors
{
    public const string NotSupportedCode = ErrorCodes.Rights.NotSupported;
    public const string UserNotFoundCode = ErrorCodes.Rights.UserNotFound;
    public const string GroupNotFoundCode = ErrorCodes.Rights.GroupNotFound;
    public const string GroupNameRequiredCode = ErrorCodes.Rights.GroupNameRequired;
    public const string GroupNameTooLongCode = ErrorCodes.Rights.GroupNameTooLong;
    public const string SubjectNotFoundCode = ErrorCodes.Rights.SubjectNotFound;
    public const string RoleUnknownCode = ErrorCodes.Rights.RoleUnknown;
    public const string ScopeNotFoundCode = ErrorCodes.Rights.ScopeNotFound;
    public const string ScopeTooWideCode = ErrorCodes.Rights.ScopeTooWide;
    public const string ExpiryRequiredCode = ErrorCodes.Rights.ExpiryRequired;
    public const string ExpiryInPastCode = ErrorCodes.Rights.ExpiryInPast;
    public const string AssignmentNotFoundCode = ErrorCodes.Rights.AssignmentNotFound;
    public const string StateInvalidCode = ErrorCodes.Rights.StateInvalid;

    public static Error NotSupported => new(NotSupportedCode, ErrorKind.Conflict);

    public static Error UserNotFound => Error.NotFound(UserNotFoundCode);

    public static Error GroupNotFound => Error.NotFound(GroupNotFoundCode);

    public static Error GroupNameRequired => Error.Validation(GroupNameRequiredCode, "Name");

    public static Error GroupNameTooLong => Error.Validation(GroupNameTooLongCode, "Name");

    public static Error SubjectNotFound => Error.Validation(SubjectNotFoundCode, "SubjectId");

    public static Error RoleUnknown => Error.Validation(RoleUnknownCode, "Role");

    public static Error ScopeNotFound => Error.Validation(ScopeNotFoundCode, "ScopeId");

    public static Error ScopeTooWide => Error.Validation(ScopeTooWideCode, "ScopeKind");

    public static Error ExpiryRequired => Error.Validation(ExpiryRequiredCode, "ExpiresAt");

    public static Error ExpiryInPast => Error.Validation(ExpiryInPastCode, "ExpiresAt");

    public static Error AssignmentNotFound => Error.NotFound(AssignmentNotFoundCode);

    public static Error StateInvalid => Error.Validation(StateInvalidCode, "State");
}

/// <summary>Poste isolé et registre en configuration : rien à administrer, chaque écriture est refusée.</summary>
internal sealed class NoAuthorizationAdministration : IAuthorizationAdministration
{
    public Task<Result> SetAccountAsync(OrganisationId cabinet, Guid userId, AccountState state, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result<GroupId>> CreateGroupAsync(OrganisationId cabinet, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Result<GroupId>.Failure(RightsErrors.NotSupported));

    public Task<Result> SetGroupMemberAsync(GroupId group, Guid userId, bool member, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<Result> SetDossierPlacementAsync(GroupId group, DossierId dossier, bool placed, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(RightsErrors.NotSupported));

    public Task<RoleDefinition?> FindCustomRoleAsync(OrganisationId cabinet, string key, CancellationToken cancellationToken) =>
        Task.FromResult<RoleDefinition?>(null);

    public Task<Result<Guid>> AssignAsync(OrganisationId cabinet, RoleAssignment assignment, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Guid>.Failure(RightsErrors.NotSupported));

    public Task<Result<SubjectRef>> RevokeAsync(OrganisationId cabinet, Guid assignmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<SubjectRef>.Failure(RightsErrors.NotSupported));
}
