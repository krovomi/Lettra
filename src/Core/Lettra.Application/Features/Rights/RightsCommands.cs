using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Authorization;
using Lettra.Contracts.Rights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Rights;

// Administration des droits du cabinet (annexe F §3). Chaque écriture invalide les permissions en cache des
// utilisateurs touchés : un retrait prend effet tout de suite, sans attendre l'expiration du cache.

/// <summary>Déclare un compte dans le cabinet ou change son état (suspendre, réactiver).</summary>
public sealed record SetUserAccountCommand(OrganisationId Organisation, Guid UserId, AccountState State)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

public sealed record CreateGroupCommand(OrganisationId Organisation, string Name)
    : ICommand<Result<GroupSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

/// <summary>Ajoute (<paramref name="Member"/> vrai) ou retire un membre du groupe.</summary>
public sealed record SetGroupMemberCommand(GroupId Group, Guid UserId, bool Member)
    : ICommand<Result>, IRequireGroupPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

/// <summary>
/// Place un dossier dans le groupe, ou l'en retire. Gérer le groupe ne suffit pas : il faut aussi administrer le
/// dossier, sinon un administrateur de groupe ferait entrer n'importe quel dossier du cabinet dans son périmètre.
/// </summary>
public sealed record SetDossierPlacementCommand(GroupId Group, DossierId Dossier, bool Placed)
    : ICommand<Result>, IRequireGroupPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

public sealed record AssignRoleCommand(
    OrganisationId Organisation,
    SubjectRef Subject,
    string Role,
    AuthorizationScope Scope,
    DateTimeOffset? ExpiresAt) : ICommand<Result<RoleAssignmentSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

public sealed record RevokeRoleCommand(OrganisationId Organisation, Guid AssignmentId)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

internal sealed class SetUserAccountHandler(IAuthorizationAdministration administration, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetUserAccountCommand, Result>
{
    public async Task<Result> HandleAsync(SetUserAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // La suppression d'un compte suit la procédure RGPD, pas l'administration courante.
        if (!Enum.IsDefined(command.State) || command.State == AccountState.Deleted || command.UserId == Guid.Empty)
        {
            return RightsErrors.StateInvalid;
        }

        Result result = await administration.SetAccountAsync(command.Organisation, command.UserId, command.State, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await cache.InvalidateUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}

internal sealed class CreateGroupHandler(IAuthorizationAdministration administration)
    : ICommandHandler<CreateGroupCommand, Result<GroupSummary>>
{
    public async Task<Result<GroupSummary>> HandleAsync(CreateGroupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return RightsErrors.GroupNameRequired;
        }

        if (name.Length > CreateGroupRequest.MaxNameLength)
        {
            return RightsErrors.GroupNameTooLong;
        }

        Result<GroupId> created = await administration.CreateGroupAsync(command.Organisation, name, cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? new GroupSummary(created.Value.Value, name)
            : Result<GroupSummary>.Failure(created.Errors);
    }
}

internal sealed class SetGroupMemberHandler(IAuthorizationAdministration administration, IPermissionCacheInvalidator cache)
    : ICommandHandler<SetGroupMemberCommand, Result>
{
    public async Task<Result> HandleAsync(SetGroupMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result result = await administration.SetGroupMemberAsync(command.Group, command.UserId, command.Member, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await cache.InvalidateUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}

internal sealed class SetDossierPlacementHandler(
    IAuthorizationAdministration administration,
    ICurrentUser user,
    IPermissionService permissions,
    IPermissionCacheInvalidator cache) : ICommandHandler<SetDossierPlacementCommand, Result>
{
    public async Task<Result> HandleAsync(SetDossierPlacementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlySet<Permission> onDossier = await permissions.GetEffectiveAsync(user.UserId, command.Dossier, cancellationToken).ConfigureAwait(false);
        if (!onDossier.Contains(Permission.DossierAdministrer))
        {
            return SecurityErrors.Forbidden;
        }

        Result result = await administration.SetDossierPlacementAsync(command.Group, command.Dossier, command.Placed, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            // Tous les membres du groupe, et tous ceux qui ont un rôle sur lui, gagnent ou perdent le dossier.
            await cache.InvalidateAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}

internal sealed class AssignRoleHandler(
    IAuthorizationAdministration administration,
    IAuthorizationDirectory directory,
    IPermissionCacheInvalidator cache,
    TimeProvider clock) : ICommandHandler<AssignRoleCommand, Result<RoleAssignmentSummary>>
{
    public async Task<Result<RoleAssignmentSummary>> HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Subject.Kind) || command.Subject.Id == Guid.Empty)
        {
            return RightsErrors.SubjectNotFound;
        }

        string key = command.Role?.Trim() ?? string.Empty;
        RoleDefinition? role = RoleTemplates.All.GetValueOrDefault(key)
            ?? await administration.FindCustomRoleAsync(command.Organisation, key, cancellationToken).ConfigureAwait(false);

        // Un cabinet n'attribue jamais un rôle de la plateforme.
        if (role is null || role.WidestScope == ScopeKind.Plateforme)
        {
            return RightsErrors.RoleUnknown;
        }

        if (command.Scope.Kind is not (ScopeKind.Cabinet or ScopeKind.Groupe or ScopeKind.Dossier))
        {
            return RightsErrors.ScopeNotFound;
        }

        if (command.Scope.Kind < role.WidestScope)
        {
            return RightsErrors.ScopeTooWide;
        }

        if (command.ExpiresAt is DateTimeOffset expires ? expires <= clock.GetUtcNow() : role.RequiresExpiry)
        {
            return command.ExpiresAt is null ? RightsErrors.ExpiryRequired : RightsErrors.ExpiryInPast;
        }

        ScopePath? path = await directory.GetScopePathAsync(command.Scope, cancellationToken).ConfigureAwait(false);
        if (path?.Cabinet != command.Organisation)
        {
            return RightsErrors.ScopeNotFound;
        }

        RoleAssignment assignment = new(command.Subject, key, command.Scope, command.ExpiresAt);
        Result<Guid> saved = await administration.AssignAsync(command.Organisation, assignment, cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<RoleAssignmentSummary>.Failure(saved.Errors);
        }

        await RightsCache.InvalidateAsync(cache, command.Subject, cancellationToken).ConfigureAwait(false);
        return new RoleAssignmentSummary(
            saved.Value,
            (SubjectKindDto)command.Subject.Kind,
            command.Subject.Id,
            key,
            (ScopeKindDto)command.Scope.Kind,
            command.Scope.Id,
            command.ExpiresAt);
    }
}

internal sealed class RevokeRoleHandler(IAuthorizationAdministration administration, IPermissionCacheInvalidator cache)
    : ICommandHandler<RevokeRoleCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<SubjectRef> revoked = await administration.RevokeAsync(command.Organisation, command.AssignmentId, cancellationToken).ConfigureAwait(false);
        if (!revoked.IsSuccess)
        {
            return Result.Failure(revoked.Errors);
        }

        await RightsCache.InvalidateAsync(cache, revoked.Value, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

internal static class RightsCache
{
    /// <summary>Un utilisateur : ses seuls droits. Un groupe : tous ses membres, donc tout le cache.</summary>
    public static ValueTask InvalidateAsync(IPermissionCacheInvalidator cache, SubjectRef subject, CancellationToken cancellationToken) =>
        subject.Kind == SubjectKind.User
            ? cache.InvalidateUserAsync(subject.Id, cancellationToken)
            : cache.InvalidateAllAsync(cancellationToken);
}
