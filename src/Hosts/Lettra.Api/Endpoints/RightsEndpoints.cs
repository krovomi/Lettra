using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Rights;
using Lettra.Contracts.Rights;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Administration des droits (annexe F §3) : comptes, groupes et attributions sur le cabinet de la route ; membres
/// et dossiers d'un groupe sur le groupe de la route.
/// </summary>
internal static class RightsEndpoints
{
    public static RouteGroupBuilder MapCabinetRights(this RouteGroupBuilder cabinet)
    {
        cabinet.MapPut("/utilisateurs/{userId:guid}", SetAccountAsync)
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithTags("Droits")
            .WithName("SetUserAccount")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        cabinet.MapPost("/groupes", CreateGroupAsync)
            .RequireCabinetPermission(Permission.GroupeGerer)
            .WithTags("Droits")
            .WithName("CreateGroup")
            .Produces<GroupSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cabinet.MapPost("/attributions", AssignAsync)
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithTags("Droits")
            .WithName("AssignRole")
            .Produces<RoleAssignmentSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cabinet.MapDelete("/attributions/{assignmentId:guid}", RevokeAsync)
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithTags("Droits")
            .WithName("RevokeRole")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Espace d'administration : lecture du registre, personnes, équipes et rôles sur mesure.
        cabinet.MapCabinetAdministration();
        return cabinet;
    }

    public static RouteGroupBuilder MapGroupRights(this RouteGroupBuilder group)
    {
        group.MapPut("/membres/{userId:guid}", (Guid groupId, Guid userId, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SetGroupMemberCommand(new GroupId(groupId), userId, Member: true), ct))
            .RequireGroupPermission(Permission.GroupeGerer)
            .WithName("AddGroupMember");
        group.MapDelete("/membres/{userId:guid}", (Guid groupId, Guid userId, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SetGroupMemberCommand(new GroupId(groupId), userId, Member: false), ct))
            .RequireGroupPermission(Permission.GroupeGerer)
            .WithName("RemoveGroupMember");
        group.MapPut("/dossiers/{dossierId:guid}", (Guid groupId, Guid dossierId, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SetDossierPlacementCommand(new GroupId(groupId), new DossierId(dossierId), Placed: true), ct))
            .RequireGroupPermission(Permission.GroupeGerer)
            .WithName("PlaceDossierInGroup");
        group.MapDelete("/dossiers/{dossierId:guid}", (Guid groupId, Guid dossierId, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new SetDossierPlacementCommand(new GroupId(groupId), new DossierId(dossierId), Placed: false), ct))
            .RequireGroupPermission(Permission.GroupeGerer)
            .WithName("RemoveDossierFromGroup");
        group.WithTags("Droits");
        return group;
    }

    private static Task<IResult> SetAccountAsync(
        Guid cabinetId, Guid userId, SetUserAccountRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(commands, new SetUserAccountCommand(new OrganisationId(cabinetId), userId, (AccountState)request.State), cancellationToken);
    }

    private static async Task<IResult> CreateGroupAsync(
        Guid cabinetId, CreateGroupRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<GroupSummary> created = await commands
            .SendAsync(new CreateGroupCommand(new OrganisationId(cabinetId), request.Name), cancellationToken).ConfigureAwait(false);
        return created.IsSuccess
            ? TypedResults.Created($"/api/v1/groupes/{created.Value.Id}", created.Value)
            : created.Errors.ToProblem();
    }

    private static async Task<IResult> AssignAsync(
        Guid cabinetId, AssignRoleRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AssignRoleCommand command = new(
            new OrganisationId(cabinetId),
            new SubjectRef((SubjectKind)request.SubjectKind, request.SubjectId),
            request.Role,
            new AuthorizationScope((ScopeKind)request.ScopeKind, request.ScopeId),
            request.ExpiresAt);
        Result<RoleAssignmentSummary> assigned = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return assigned.IsSuccess
            ? TypedResults.Created($"/api/v1/cabinets/{cabinetId}/attributions/{assigned.Value.Id}", assigned.Value)
            : assigned.Errors.ToProblem();
    }

    private static Task<IResult> RevokeAsync(Guid cabinetId, Guid assignmentId, ICommandDispatcher commands, CancellationToken cancellationToken) =>
        SendAsync(commands, new RevokeRoleCommand(new OrganisationId(cabinetId), assignmentId), cancellationToken);

    private static async Task<IResult> SendAsync<TCommand>(ICommandDispatcher commands, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<Result>
    {
        Result result = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.NoContent() : result.Errors.ToProblem();
    }
}
