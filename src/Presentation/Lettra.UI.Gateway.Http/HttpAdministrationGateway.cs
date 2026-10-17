using System.Net.Http.Json;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Contracts.Rights;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Routes <c>/api/v1/cabinets/{id}/…</c>, <c>/api/v1/groupes/{id}/…</c> et <c>/api/v1/dossiers/acces</c>.</summary>
internal sealed class HttpAdministrationGateway(HttpClient http) : IAdministrationGateway
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<OperationResult<MyAccessResponse>> GetMyAccessAsync(Guid? dossierId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(dossierId is Guid id ? HttpCalls.Relative($"api/v1/dossiers/acces?dossier={id}") : HttpCalls.Relative($"api/v1/dossiers/acces"), ct),
            Json.MyAccessResponse,
            cancellationToken);

    public Task<OperationResult<AdministrationOverview>> GetOverviewAsync(Guid cabinetId, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(ct => http.GetAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/administration"), ct), Json.AdministrationOverview, cancellationToken);

    public Task<OperationResult<PersonSummary>> InviteAsync(Guid cabinetId, InvitePersonRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/personnes"), request, Json.InvitePersonRequest, ct),
            Json.PersonSummary,
            cancellationToken);

    public Task<OperationResult<bool>> UpdatePersonAsync(Guid cabinetId, Guid userId, UpdatePersonRequest request, CancellationToken cancellationToken) =>
        NoContentAsync(ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/personnes/{userId}"), request, Json.UpdatePersonRequest, ct), cancellationToken);

    public Task<OperationResult<bool>> SetAccountStateAsync(Guid cabinetId, Guid userId, AccountStateDto state, CancellationToken cancellationToken) =>
        NoContentAsync(
            ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/utilisateurs/{userId}"), new SetUserAccountRequest { State = state }, Json.SetUserAccountRequest, ct),
            cancellationToken);

    public Task<OperationResult<RoleAssignmentSummary>> AssignAsync(Guid cabinetId, AssignRoleRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/attributions"), request, Json.AssignRoleRequest, ct),
            Json.RoleAssignmentSummary,
            cancellationToken);

    public Task<OperationResult<bool>> RevokeAsync(Guid cabinetId, Guid assignmentId, CancellationToken cancellationToken) =>
        NoContentAsync(ct => http.DeleteAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/attributions/{assignmentId}"), ct), cancellationToken);

    public Task<OperationResult<GroupSummary>> CreateTeamAsync(Guid cabinetId, CreateGroupRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/groupes"), request, Json.CreateGroupRequest, ct),
            Json.GroupSummary,
            cancellationToken);

    public Task<OperationResult<bool>> RenameTeamAsync(Guid cabinetId, Guid teamId, RenameTeamRequest request, CancellationToken cancellationToken) =>
        NoContentAsync(ct => http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/groupes/{teamId}"), request, Json.RenameTeamRequest, ct), cancellationToken);

    public Task<OperationResult<bool>> DeleteTeamAsync(Guid cabinetId, Guid teamId, CancellationToken cancellationToken) =>
        NoContentAsync(ct => http.DeleteAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/groupes/{teamId}"), ct), cancellationToken);

    public Task<OperationResult<bool>> SetTeamMemberAsync(Guid teamId, Guid userId, bool member, CancellationToken cancellationToken) =>
        NoContentAsync(
            ct => member
                ? http.PutAsync(HttpCalls.Relative($"api/v1/groupes/{teamId}/membres/{userId}"), null, ct)
                : http.DeleteAsync(HttpCalls.Relative($"api/v1/groupes/{teamId}/membres/{userId}"), ct),
            cancellationToken);

    public Task<OperationResult<bool>> SetTeamDossierAsync(Guid teamId, Guid dossierId, bool placed, CancellationToken cancellationToken) =>
        NoContentAsync(
            ct => placed
                ? http.PutAsync(HttpCalls.Relative($"api/v1/groupes/{teamId}/dossiers/{dossierId}"), null, ct)
                : http.DeleteAsync(HttpCalls.Relative($"api/v1/groupes/{teamId}/dossiers/{dossierId}"), ct),
            cancellationToken);

    public Task<OperationResult<RoleDescription>> SaveRoleAsync(Guid cabinetId, string? key, SaveCustomRoleRequest request, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => key is null
                ? http.PostAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/roles"), request, Json.SaveCustomRoleRequest, ct)
                : http.PutAsJsonAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/roles/{Uri.EscapeDataString(key)}"), request, Json.SaveCustomRoleRequest, ct),
            Json.RoleDescription,
            cancellationToken);

    public Task<OperationResult<bool>> DeleteRoleAsync(Guid cabinetId, string key, CancellationToken cancellationToken) =>
        NoContentAsync(ct => http.DeleteAsync(HttpCalls.Relative($"api/v1/cabinets/{cabinetId}/roles/{Uri.EscapeDataString(key)}"), ct), cancellationToken);

    /// <summary>204 : réussite sans corps ; sinon le problème du serveur, comme partout ailleurs.</summary>
    private static async Task<OperationResult<bool>> NoContentAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await send(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return OperationResult<bool>.Success(true);
            }

            OperationResult<ProblemResponse> failure = await response.ToOperationResultAsync(Json.ProblemResponse, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.Failure(failure.Errors);
        }
        catch (HttpRequestException)
        {
            return OperationResult<bool>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}
