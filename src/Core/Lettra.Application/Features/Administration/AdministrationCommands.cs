using System.Net.Mail;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Authorization;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Rights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Administration;

// Écritures de l'espace d'administration (personnes, équipes, rôles sur mesure). Les états de compte, les membres des
// équipes et les attributions restent sur les cas d'usage de Features/Rights, que l'interface appelle tels quels.

/// <summary>Ajoute une personne au cabinet, à l'état « invitée » : elle n'a aucun droit avant sa première connexion.</summary>
public sealed record InvitePersonCommand(OrganisationId Organisation, string DisplayName, string Email)
    : ICommand<Result<PersonSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

public sealed record UpdatePersonCommand(OrganisationId Organisation, Guid UserId, string DisplayName, string Email)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

public sealed record RenameTeamCommand(OrganisationId Organisation, GroupId Team, string Name)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

/// <summary>Supprime une équipe : ses membres et ses dossiers restent dans le cabinet, ils perdent ce que l'équipe leur donnait.</summary>
public sealed record DeleteTeamCommand(OrganisationId Organisation, GroupId Team)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.GroupeGerer;
}

/// <summary>
/// Crée (<paramref name="Key"/> nul) ou modifie un rôle sur mesure. Composer un rôle engage tout le cabinet : il faut
/// administrer le cabinet, pas seulement gérer les personnes.
/// </summary>
public sealed record SaveCustomRoleCommand(OrganisationId Organisation, string? Key, string Name, string? Description, IReadOnlyList<AreaAccess> Areas)
    : ICommand<Result<RoleDescription>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

public sealed record DeleteCustomRoleCommand(OrganisationId Organisation, string Key)
    : ICommand<Result>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>Règles de saisie partagées : nom et adresse d'une personne.</summary>
internal static class PersonRules
{
    public static Result<(string Name, string Email)> Normalize(string? displayName, string? email)
    {
        string name = displayName?.Trim() ?? string.Empty;
        string address = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0)
        {
            return AdministrationErrors.NameRequired;
        }

        if (name.Length > InvitePersonRequest.MaxNameLength)
        {
            return AdministrationErrors.NameTooLong;
        }

        if (!IsEmail(address))
        {
            return AdministrationErrors.EmailInvalid;
        }

        return (name, address);
    }

    /// <summary>Une adresse simple (local@domaine.tld), sans nom affiché ni espace.</summary>
    private static bool IsEmail(string address)
    {
        if (address.Length is 0 or > InvitePersonRequest.MaxEmailLength || address.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return MailAddress.TryCreate(address, out MailAddress? parsed)
            && string.Equals(parsed.Address, address, StringComparison.Ordinal)
            && parsed.Host.Contains('.', StringComparison.Ordinal)
            && !parsed.Host.EndsWith('.');
    }
}

internal sealed class InvitePersonHandler(IAuthorizationRegistry registry, TimeProvider clock)
    : ICommandHandler<InvitePersonCommand, Result<PersonSummary>>
{
    public async Task<Result<PersonSummary>> HandleAsync(InvitePersonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<(string Name, string Email)> valid = PersonRules.Normalize(command.DisplayName, command.Email);
        if (valid.IsFailure)
        {
            return Result<PersonSummary>.Failure(valid.Errors);
        }

        Result<Guid> invited = await registry.InviteAsync(command.Organisation, valid.Value.Name, valid.Value.Email, cancellationToken).ConfigureAwait(false);
        return invited.IsSuccess
            ? new PersonSummary(invited.Value, valid.Value.Name, valid.Value.Email, AccountStateDto.Invited, clock.GetUtcNow(), [], [], IsMe: false)
            : Result<PersonSummary>.Failure(invited.Errors);
    }
}

internal sealed class UpdatePersonHandler(IAuthorizationRegistry registry) : ICommandHandler<UpdatePersonCommand, Result>
{
    public async Task<Result> HandleAsync(UpdatePersonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result<(string Name, string Email)> valid = PersonRules.Normalize(command.DisplayName, command.Email);
        return valid.IsFailure
            ? Result.Failure(valid.Errors)
            : await registry.UpdatePersonAsync(command.Organisation, command.UserId, valid.Value.Name, valid.Value.Email, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RenameTeamHandler(IAuthorizationRegistry registry) : ICommandHandler<RenameTeamCommand, Result>
{
    public async Task<Result> HandleAsync(RenameTeamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return RightsErrors.GroupNameRequired;
        }

        return name.Length > CreateGroupRequest.MaxNameLength
            ? RightsErrors.GroupNameTooLong
            : await registry.RenameGroupAsync(command.Organisation, command.Team, name, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class DeleteTeamHandler(IAuthorizationRegistry registry, IPermissionCacheInvalidator cache) : ICommandHandler<DeleteTeamCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteTeamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Result deleted = await registry.DeleteGroupAsync(command.Organisation, command.Team, cancellationToken).ConfigureAwait(false);
        if (deleted.IsSuccess)
        {
            await cache.InvalidateAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }
}

internal sealed class SaveCustomRoleHandler(IAuthorizationRegistry registry, IPermissionCacheInvalidator cache)
    : ICommandHandler<SaveCustomRoleCommand, Result<RoleDescription>>
{
    /// <summary>Préfixe des clés de rôles sur mesure : jamais confondues avec un rôle fourni.</summary>
    public const string KeyPrefix = "c-";

    public async Task<Result<RoleDescription>> HandleAsync(SaveCustomRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string name = command.Name?.Trim() ?? string.Empty;
        string? description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        if (name.Length == 0)
        {
            return AdministrationErrors.RoleNameRequired;
        }

        if (name.Length > SaveCustomRoleRequest.MaxNameLength)
        {
            return AdministrationErrors.RoleNameTooLong;
        }

        if (description?.Length > SaveCustomRoleRequest.MaxDescriptionLength)
        {
            return AdministrationErrors.RoleDescriptionTooLong;
        }

        IReadOnlySet<Permission>? permissions = AccessAreaPermissions.Compose(command.Areas ?? []);
        if (permissions is null)
        {
            return AdministrationErrors.RoleAreaInvalid;
        }

        if (permissions.Count == 0)
        {
            return AdministrationErrors.RoleEmpty;
        }

        string key;
        int uses = 0;
        if (command.Key is null)
        {
            key = KeyPrefix + Guid.CreateVersion7().ToString("N");
        }
        else
        {
            CabinetRegistry? read = await registry.ReadAsync(command.Organisation, cancellationToken).ConfigureAwait(false);
            if (read?.CustomRoles.Any(r => string.Equals(r.Definition.Key, command.Key, StringComparison.Ordinal)) != true)
            {
                return AdministrationErrors.RoleNotFound;
            }

            key = command.Key;
            uses = read.Assignments.Count(a => string.Equals(a.Assignment.Role, key, StringComparison.Ordinal));
        }

        // Portée la plus large : le cabinet. L'administrateur choisit ensuite, pour chaque personne, où le rôle s'applique.
        RoleDefinition definition = new(key, permissions, ScopeKind.Cabinet);
        Result saved = await registry.SaveCustomRoleAsync(command.Organisation, new RegistryCustomRole(definition, name, description), cancellationToken)
            .ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return Result<RoleDescription>.Failure(saved.Errors);
        }

        await cache.InvalidateAllAsync(cancellationToken).ConfigureAwait(false);
        return AdministrationOverviewBuilder.Describe(definition, isCustom: true, name, description, new Dictionary<string, int> { [key] = uses });
    }
}

internal sealed class DeleteCustomRoleHandler(IAuthorizationRegistry registry) : ICommandHandler<DeleteCustomRoleCommand, Result>
{
    public Task<Result> HandleAsync(DeleteCustomRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return string.IsNullOrWhiteSpace(command.Key) || !command.Key.StartsWith(SaveCustomRoleHandler.KeyPrefix, StringComparison.Ordinal)
            ? Task.FromResult(Result.Failure(AdministrationErrors.RoleNotFound))
            : registry.DeleteCustomRoleAsync(command.Organisation, command.Key, cancellationToken);
    }
}
