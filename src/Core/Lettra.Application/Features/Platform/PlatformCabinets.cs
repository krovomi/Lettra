using System.Globalization;
using System.Text;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Authorization;
using Lettra.Application.Features.Administration;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Platform;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Platform;

/// <summary>Cas d'usage de l'administrateur de la plateforme : portée « plateforme », jamais un cabinet ni un dossier.</summary>
public abstract record PlatformUseCase : IRequireScopedPermission
{
    public Permission Permission => Permission.PlateformeAdministrer;

    public AuthorizationScope Scope => AuthorizationScope.Plateforme;
}

/// <summary>Les cabinets de la plateforme, avec des compteurs de dossiers et les créations en échec.</summary>
public sealed record ListPlatformCabinetsQuery : PlatformUseCase, IQuery<Result<PlatformOverviewResponse>>;

/// <summary>
/// Arrivée d'un cabinet, sans script (plan du mode 5, étape 2) : organisation chez le fournisseur d'identité, inscription
/// au catalogue dans sa cellule avec sa clé, compte de son premier administrateur avec l'e-mail pour choisir son mot de
/// passe. Chaque étape est idempotente : une arrivée interrompue se relance avec la même demande.
/// </summary>
public sealed record OnboardCabinetCommand(OnboardCabinetRequest Request) : PlatformUseCase, ICommand<Result<OnboardCabinetResponse>>;

/// <summary>
/// Départ d'un cabinet : il continue de travailler et d'exporter ses données (FEC, archives) jusqu'à la fin du délai de
/// rétractation ; ensuite ses dossiers sont effacés, sa clé détruite et ses comptes supprimés.
/// </summary>
public sealed record ScheduleCabinetDepartureCommand(OrganisationId Organisation) : PlatformUseCase, ICommand<Result<PlatformCabinetResponse>>;

public sealed record CancelCabinetDepartureCommand(OrganisationId Organisation) : PlatformUseCase, ICommand<Result<PlatformCabinetResponse>>;

/// <summary>Relance les créations de dossiers en échec, sans attendre la prochaine passe du Worker.</summary>
public sealed record RetryFailedDossiersCommand : PlatformUseCase, ICommand<Result<DossierRetryResponse>>;

internal sealed class ListPlatformCabinetsHandler(ICabinetLifecycle lifecycle, IIdentityProvisioning identity)
    : IQueryHandler<ListPlatformCabinetsQuery, Result<PlatformOverviewResponse>>
{
    public async Task<Result<PlatformOverviewResponse>> HandleAsync(ListPlatformCabinetsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> cells = await lifecycle.ListCellsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PlatformCabinetResponse> cabinets = await lifecycle.ListAsync(cancellationToken).ConfigureAwait(false);
        return new PlatformOverviewResponse(lifecycle.IsAvailable && identity.IsConfigured && cells.Count > 0, cells, cabinets);
    }
}

internal sealed class OnboardCabinetHandler(
    ICabinetLifecycle lifecycle,
    IIdentityProvisioning identity,
    IAuthorizationRegistry registry,
    IAuthorizationAdministration administration)
    : ICommandHandler<OnboardCabinetCommand, Result<OnboardCabinetResponse>>
{
    public async Task<Result<OnboardCabinetResponse>> HandleAsync(OnboardCabinetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string name = command.Request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return PlatformErrors.NameRequired;
        }

        if (name.Length > OnboardCabinetRequest.MaxNameLength)
        {
            return PlatformErrors.NameTooLong;
        }

        Result<(string Name, string Email)> administrator = PersonRules.Normalize(command.Request.AdministratorName, command.Request.AdministratorEmail);
        if (administrator.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(administrator.Errors);
        }

        if (!lifecycle.IsAvailable || !identity.IsConfigured)
        {
            return PlatformErrors.Unavailable;
        }

        Result<string> cell = await CellAsync(command.Request.CellId, cancellationToken).ConfigureAwait(false);
        if (cell.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(cell.Errors);
        }

        // 1. L'organisation chez le fournisseur d'identité : son identifiant devient celui du cabinet.
        Result<OrganisationId> organisation = await identity.EnsureOrganisationAsync(PlatformNames.Alias(name), name, cancellationToken).ConfigureAwait(false);
        if (organisation.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(organisation.Errors);
        }

        // 2. Le cabinet au catalogue, dans sa cellule, avec sa clé.
        Result<PlatformCabinetResponse> registered = await lifecycle.RegisterAsync(organisation.Value, name, cell.Value, cancellationToken).ConfigureAwait(false);
        if (registered.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(registered.Errors);
        }

        // 3. Le premier administrateur : son compte, sa place au registre, son rôle sur le cabinet.
        Result<ProvisionedAccount> account = await identity.EnsureMemberAsync(organisation.Value, administrator.Value.Email, administrator.Value.Name, cancellationToken).ConfigureAwait(false);
        if (account.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(account.Errors);
        }

        Result granted = await GrantAdministratorAsync(organisation.Value, account.Value.UserId, administrator.Value, cancellationToken).ConfigureAwait(false);
        if (granted.IsFailure)
        {
            return Result<OnboardCabinetResponse>.Failure(granted.Errors);
        }

        return new OnboardCabinetResponse(
            registered.Value,
            account.Value.UserId,
            account.Value.InvitationSent ? InvitationEmailStatusDto.Sent : InvitationEmailStatusDto.Failed);
    }

    private async Task<Result<string>> CellAsync(string? requested, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> cells = await lifecycle.ListCellsAsync(cancellationToken).ConfigureAwait(false);
        string? wanted = string.IsNullOrWhiteSpace(requested) ? (cells.Count == 1 ? cells[0] : null) : requested.Trim();
        return wanted is not null && cells.Contains(wanted, StringComparer.Ordinal) ? Result<string>.Success(wanted) : PlatformErrors.CellUnknown;
    }

    private async Task<Result> GrantAdministratorAsync(OrganisationId cabinet, Guid userId, (string Name, string Email) person, CancellationToken cancellationToken)
    {
        Result active = await administration.SetAccountAsync(cabinet, userId, AccountState.Active, cancellationToken).ConfigureAwait(false);
        if (active.IsFailure)
        {
            return active;
        }

        Result described = await registry.UpdatePersonAsync(cabinet, userId, person.Name, person.Email, cancellationToken).ConfigureAwait(false);
        if (described.IsFailure)
        {
            return described;
        }

        RoleAssignment role = new(new SubjectRef(SubjectKind.User, userId), RoleTemplates.AdministrateurCabinet, AuthorizationScope.ForCabinet(cabinet));
        CabinetRegistry? current = await registry.ReadAsync(cabinet, cancellationToken).ConfigureAwait(false);
        if (current?.Assignments.Any(a => a.Assignment.Subject == role.Subject && a.Assignment.Role == role.Role && a.Assignment.Scope == role.Scope) == true)
        {
            return Result.Success();
        }

        Result<Guid> assigned = await administration.AssignAsync(cabinet, role, cancellationToken).ConfigureAwait(false);
        return assigned.IsSuccess ? Result.Success() : Result.Failure(assigned.Errors);
    }
}

internal sealed class ScheduleCabinetDepartureHandler(ICabinetLifecycle lifecycle, ICurrentUser user)
    : ICommandHandler<ScheduleCabinetDepartureCommand, Result<PlatformCabinetResponse>>
{
    public Task<Result<PlatformCabinetResponse>> HandleAsync(ScheduleCabinetDepartureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.ScheduleDepartureAsync(command.Organisation, user.UserId, cancellationToken);
    }
}

internal sealed class CancelCabinetDepartureHandler(ICabinetLifecycle lifecycle)
    : ICommandHandler<CancelCabinetDepartureCommand, Result<PlatformCabinetResponse>>
{
    public Task<Result<PlatformCabinetResponse>> HandleAsync(CancelCabinetDepartureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.CancelDepartureAsync(command.Organisation, cancellationToken);
    }
}

internal sealed class RetryFailedDossiersHandler(ICabinetLifecycle lifecycle)
    : ICommandHandler<RetryFailedDossiersCommand, Result<DossierRetryResponse>>
{
    public async Task<Result<DossierRetryResponse>> HandleAsync(RetryFailedDossiersCommand command, CancellationToken cancellationToken) =>
        new DossierRetryResponse(await lifecycle.RetryFailedDossiersAsync(cancellationToken).ConfigureAwait(false));
}

/// <summary>Le nom technique d'un cabinet chez le fournisseur d'identité : son nom en minuscules, sans accents ni espaces.</summary>
internal static class PlatformNames
{
    public const int MaxAliasLength = 64;

    public static string Alias(string name)
    {
        StringBuilder alias = new(name.Length);
        foreach (char c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                alias.Append(char.ToLowerInvariant(c));
            }
            else if (alias.Length > 0 && alias[^1] != '-')
            {
                alias.Append('-');
            }
        }

        string trimmed = alias.ToString().Trim('-');
        trimmed = trimmed.Length > MaxAliasLength ? trimmed[..MaxAliasLength].TrimEnd('-') : trimmed;
        return trimmed.Length == 0 ? "cabinet" : trimmed;
    }
}

/// <summary>Hôtes sans catalogue (poste isolé) : la plateforme n'y héberge aucun cabinet.</summary>
internal sealed class NoCabinetLifecycle : ICabinetLifecycle
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<string>> ListCellsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<PlatformCabinetResponse>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformCabinetResponse>>([]);

    public Task<Result<PlatformCabinetResponse>> RegisterAsync(OrganisationId organisation, string name, string cellId, CancellationToken cancellationToken) => Unavailable();

    public Task<Result<PlatformCabinetResponse>> ScheduleDepartureAsync(OrganisationId organisation, Guid requestedBy, CancellationToken cancellationToken) => Unavailable();

    public Task<Result<PlatformCabinetResponse>> CancelDepartureAsync(OrganisationId organisation, CancellationToken cancellationToken) => Unavailable();

    public Task<int> RetryFailedDossiersAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    private static Task<Result<PlatformCabinetResponse>> Unavailable() => Task.FromResult<Result<PlatformCabinetResponse>>(PlatformErrors.Unavailable);
}

/// <summary>Aucun fournisseur d'identité réglé pour la plateforme : les cabinets s'y déclarent dans la configuration.</summary>
internal sealed class NoIdentityProvisioning : IIdentityProvisioning
{
    public bool IsConfigured => false;

    public Task<Result<OrganisationId>> EnsureOrganisationAsync(string alias, string name, CancellationToken cancellationToken) =>
        Task.FromResult<Result<OrganisationId>>(PlatformErrors.Unavailable);

    public Task<Result<ProvisionedAccount>> EnsureMemberAsync(OrganisationId organisation, string email, string displayName, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ProvisionedAccount>>(PlatformErrors.Unavailable);

    public Task DeleteOrganisationAsync(OrganisationId organisation, CancellationToken cancellationToken) => Task.CompletedTask;
}
