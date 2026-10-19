using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Administration;
using Lettra.Contracts.Administration;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Espace d'administration (« Équipe et accès ») : une lecture de tout le registre du cabinet, puis les écritures propres
/// à cet espace. Les états de compte, les membres des équipes et les attributions passent par <see cref="RightsEndpoints"/>.
/// </summary>
internal static class AdministrationEndpoints
{
    /// <summary>Routes du cabinet (<c>/api/v1/cabinets/{cabinetId}/…</c>).</summary>
    public static RouteGroupBuilder MapCabinetAdministration(this RouteGroupBuilder cabinet)
    {
        // Un administrateur d'équipe y entre aussi : le cas d'usage réduit la réponse à ce qu'il administre.
        cabinet.MapGet("/administration", (Guid cabinetId, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetAdministrationOverviewQuery(new OrganisationId(cabinetId)), ct))
            .FilterByPermission(Permission.GroupeGerer)
            .WithName("GetAdministrationOverview")
            .Produces<AdministrationOverview>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cabinet.MapPost("/personnes", InviteAsync)
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithName("InvitePerson")
            .Produces<PersonSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cabinet.MapPost("/personnes/{userId:guid}/invitation", async (Guid cabinetId, Guid userId, ICommandDispatcher commands, CancellationToken ct) =>
            {
                Result<InvitationEmailResponse> result = await commands
                    .SendAsync(new ResendInvitationCommand(new OrganisationId(cabinetId), userId), ct).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
            })
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithName("ResendInvitation")
            .Produces<InvitationEmailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cabinet.MapPut("/personnes/{userId:guid}", (Guid cabinetId, Guid userId, UpdatePersonRequest request, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new UpdatePersonCommand(new OrganisationId(cabinetId), userId, request.DisplayName, request.Email), ct))
            .RequireCabinetPermission(Permission.UtilisateursGerer)
            .WithName("UpdatePerson")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        cabinet.MapPut("/groupes/{groupId:guid}", (Guid cabinetId, Guid groupId, RenameTeamRequest request, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new RenameTeamCommand(new OrganisationId(cabinetId), new GroupId(groupId), request.Name), ct))
            .RequireCabinetPermission(Permission.GroupeGerer)
            .WithName("RenameTeam")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        cabinet.MapDelete("/groupes/{groupId:guid}", (Guid cabinetId, Guid groupId, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new DeleteTeamCommand(new OrganisationId(cabinetId), new GroupId(groupId)), ct))
            .RequireCabinetPermission(Permission.GroupeGerer)
            .WithName("DeleteTeam")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        cabinet.MapPost("/roles", (Guid cabinetId, SaveCustomRoleRequest request, ICommandDispatcher commands, CancellationToken ct) =>
                SaveRoleAsync(commands, new OrganisationId(cabinetId), null, request, ct))
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("CreateCustomRole")
            .Produces<RoleDescription>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        cabinet.MapPut("/roles/{key}", (Guid cabinetId, string key, SaveCustomRoleRequest request, ICommandDispatcher commands, CancellationToken ct) =>
                SaveRoleAsync(commands, new OrganisationId(cabinetId), key, request, ct))
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("UpdateCustomRole")
            .Produces<RoleDescription>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        cabinet.MapDelete("/roles/{key}", (Guid cabinetId, string key, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new DeleteCustomRoleCommand(new OrganisationId(cabinetId), key), ct))
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("DeleteCustomRole")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return cabinet;
    }

    /// <summary>
    /// <c>GET /api/v1/dossiers/acces?dossier=</c> : ce que l'utilisateur peut voir, avant d'avoir ouvert un dossier (un
    /// administrateur du cabinet n'ouvre aucun dossier). Le cas d'usage ne rend que les droits de l'appelant.
    /// </summary>
    public static RouteGroupBuilder MapMyAccess(this RouteGroupBuilder dossiers)
    {
        dossiers.MapGet("/acces", (IQueryDispatcher queries, CancellationToken ct, Guid? dossier = null) =>
                QueryAsync(queries, new GetMyAccessQuery(dossier is Guid id ? new DossierId(id) : null), ct))
            .FilterByPermission(Permission.DossierLire)
            .WithTags("Administration")
            .WithName("GetMyAccess")
            .Produces<MyAccessResponse>();
        return dossiers;
    }

    private static async Task<IResult> InviteAsync(
        Guid cabinetId, InvitePersonRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<PersonSummary> invited = await commands
            .SendAsync(new InvitePersonCommand(new OrganisationId(cabinetId), request.DisplayName, request.Email), cancellationToken).ConfigureAwait(false);
        return invited.IsSuccess
            ? TypedResults.Created($"/api/v1/cabinets/{cabinetId}/personnes/{invited.Value.Id}", invited.Value)
            : invited.Errors.ToProblem();
    }

    private static async Task<IResult> SaveRoleAsync(
        ICommandDispatcher commands, OrganisationId cabinet, string? key, SaveCustomRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<RoleDescription> saved = await commands
            .SendAsync(new SaveCustomRoleCommand(cabinet, key, request.Name, request.Description, request.Areas ?? []), cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
        {
            return saved.Errors.ToProblem();
        }

        return key is null
            ? TypedResults.Created($"/api/v1/cabinets/{cabinet.Value}/roles/{saved.Value.Key}", saved.Value)
            : TypedResults.Ok(saved.Value);
    }

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken)
    {
        Result<T> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> SendAsync<TCommand>(ICommandDispatcher commands, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<Result>
    {
        Result result = await commands.SendAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.NoContent() : result.Errors.ToProblem();
    }
}
