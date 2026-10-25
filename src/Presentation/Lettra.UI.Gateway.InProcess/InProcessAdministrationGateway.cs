using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Administration;
using Lettra.Application.Features.Rights;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>
/// Mêmes cas d'usage que les routes d'administration de l'API. Hors de tout dossier : un administrateur du cabinet
/// n'ouvre aucune base de dossier, chaque appel prend un scope sans résoudre de dossier.
/// </summary>
internal sealed class InProcessAdministrationGateway(IServiceScopeFactory scopes) : IAdministrationGateway
{
    public Task<OperationResult<MyAccessResponse>> GetMyAccessAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        QueryAsync(new GetMyAccessQuery(dossierId is Guid id ? new DossierId(id) : null), cancellationToken);

    public Task<OperationResult<AdministrationOverview>> GetOverviewAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        QueryAsync(new GetAdministrationOverviewQuery(new OrganisationId(cabinetId)), cancellationToken);

    public Task<OperationResult<PersonSummary>> InviteAsync(Guid cabinetId, InvitePersonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new InvitePersonCommand(new OrganisationId(cabinetId), request.DisplayName, request.Email), cancellationToken);
    }

    public Task<OperationResult<InvitationEmailResponse>> ResendInvitationAsync(Guid cabinetId, Guid userId, CancellationToken cancellationToken) =>
        SendAsync(new ResendInvitationCommand(new OrganisationId(cabinetId), userId), cancellationToken);

    public Task<OperationResult<bool>> UpdatePersonAsync(Guid cabinetId, Guid userId, UpdatePersonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new UpdatePersonCommand(new OrganisationId(cabinetId), userId, request.DisplayName, request.Email), cancellationToken);
    }

    public Task<OperationResult<bool>> SetAccountStateAsync(Guid cabinetId, Guid userId, AccountStateDto state, CancellationToken cancellationToken) =>
        SendAsync(new SetUserAccountCommand(new OrganisationId(cabinetId), userId, (AccountState)state), cancellationToken);

    public Task<OperationResult<RoleAssignmentSummary>> AssignAsync(Guid cabinetId, AssignRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(
            new AssignRoleCommand(
                new OrganisationId(cabinetId),
                new SubjectRef((SubjectKind)request.SubjectKind, request.SubjectId),
                request.Role,
                new AuthorizationScope((ScopeKind)request.ScopeKind, request.ScopeId),
                request.ExpiresAt),
            cancellationToken);
    }

    public Task<OperationResult<bool>> RevokeAsync(Guid cabinetId, Guid assignmentId, CancellationToken cancellationToken) =>
        SendAsync(new RevokeRoleCommand(new OrganisationId(cabinetId), assignmentId), cancellationToken);

    public Task<OperationResult<GroupSummary>> CreateTeamAsync(Guid cabinetId, CreateGroupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new CreateGroupCommand(new OrganisationId(cabinetId), request.Name), cancellationToken);
    }

    public Task<OperationResult<bool>> RenameTeamAsync(Guid cabinetId, Guid teamId, RenameTeamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new RenameTeamCommand(new OrganisationId(cabinetId), new GroupId(teamId), request.Name), cancellationToken);
    }

    public Task<OperationResult<bool>> DeleteTeamAsync(Guid cabinetId, Guid teamId, CancellationToken cancellationToken) =>
        SendAsync(new DeleteTeamCommand(new OrganisationId(cabinetId), new GroupId(teamId)), cancellationToken);

    public Task<OperationResult<bool>> SetTeamMemberAsync(Guid teamId, Guid userId, bool member, CancellationToken cancellationToken) =>
        SendAsync(new SetGroupMemberCommand(new GroupId(teamId), userId, member), cancellationToken);

    public Task<OperationResult<bool>> SetTeamDossierAsync(Guid teamId, Guid dossierId, bool placed, CancellationToken cancellationToken) =>
        SendAsync(new SetDossierPlacementCommand(new GroupId(teamId), new DossierId(dossierId), placed), cancellationToken);

    public Task<OperationResult<RoleDescription>> SaveRoleAsync(Guid cabinetId, string? key, SaveCustomRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(new SaveCustomRoleCommand(new OrganisationId(cabinetId), key, request.Name, request.Description, request.Areas ?? []), cancellationToken);
    }

    public Task<OperationResult<bool>> DeleteRoleAsync(Guid cabinetId, string key, CancellationToken cancellationToken) =>
        SendAsync(new DeleteCustomRoleCommand(new OrganisationId(cabinetId), key), cancellationToken);

    private Task<OperationResult<T>> QueryAsync<T>(IQuery<Result<T>> query, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.QueryAsync(sp, query, cancellationToken));

    private Task<OperationResult<T>> SendAsync<T>(ICommand<Result<T>> command, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.SendAsync(sp, command, cancellationToken));

    private Task<OperationResult<bool>> SendAsync(ICommand<Result> command, CancellationToken cancellationToken) =>
        InProcessScope.OutsideDossierAsync(scopes, sp => InProcessScope.SendAsync(sp, command, cancellationToken));
}
