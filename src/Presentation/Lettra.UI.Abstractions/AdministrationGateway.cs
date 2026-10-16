using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;

namespace Lettra.UI.Abstractions;

/// <summary>
/// L'espace « Équipe et accès » : ce que l'utilisateur peut voir, puis le registre de son cabinet et ses écritures. Deux
/// implémentations : in-process (MAUI, mode 1, en lecture seule) et HTTP (navigateur, MAUI relié à un serveur). Les
/// écritures sans valeur rendent <c>true</c>.
/// </summary>
public interface IAdministrationGateway
{
    /// <summary>Cabinet, accès à l'administration et niveaux par espace sur le dossier donné (ou sans dossier).</summary>
    Task<OperationResult<MyAccessResponse>> GetMyAccessAsync(Guid? dossierId, CancellationToken cancellationToken);

    Task<OperationResult<AdministrationOverview>> GetOverviewAsync(Guid cabinetId, CancellationToken cancellationToken);

    Task<OperationResult<PersonSummary>> InviteAsync(Guid cabinetId, InvitePersonRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> UpdatePersonAsync(Guid cabinetId, Guid userId, UpdatePersonRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> SetAccountStateAsync(Guid cabinetId, Guid userId, AccountStateDto state, CancellationToken cancellationToken);

    Task<OperationResult<RoleAssignmentSummary>> AssignAsync(Guid cabinetId, AssignRoleRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> RevokeAsync(Guid cabinetId, Guid assignmentId, CancellationToken cancellationToken);

    Task<OperationResult<GroupSummary>> CreateTeamAsync(Guid cabinetId, CreateGroupRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> RenameTeamAsync(Guid cabinetId, Guid teamId, RenameTeamRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteTeamAsync(Guid cabinetId, Guid teamId, CancellationToken cancellationToken);

    Task<OperationResult<bool>> SetTeamMemberAsync(Guid teamId, Guid userId, bool member, CancellationToken cancellationToken);

    Task<OperationResult<bool>> SetTeamDossierAsync(Guid teamId, Guid dossierId, bool placed, CancellationToken cancellationToken);

    /// <summary>Crée (<paramref name="key"/> nul) ou modifie un rôle sur mesure.</summary>
    Task<OperationResult<RoleDescription>> SaveRoleAsync(Guid cabinetId, string? key, SaveCustomRoleRequest request, CancellationToken cancellationToken);

    Task<OperationResult<bool>> DeleteRoleAsync(Guid cabinetId, string key, CancellationToken cancellationToken);
}
