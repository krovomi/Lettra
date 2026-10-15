using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Alerts;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Supervision.Application;
using Lettra.Supervision.Domain;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Centre d'alertes (annexe G §8). Deux portées : le dossier de la requête (un collaborateur y voit les alertes de son
/// dossier) et le cabinet de la route (un manager voit et traite tout le cabinet). Le pipeline revérifie chaque fois la
/// permission sur la portée, et un cas d'usage refuse une alerte située hors de cette portée.
/// </summary>
internal static class AlertEndpoints
{
    /// <summary>Routes du dossier de la requête : lecture seule.</summary>
    public static RouteGroupBuilder MapDossierAlerts(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/alerts").WithTags("Alerts");

        group.MapGet("/", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct, bool includeClosed = false) =>
                ListAsync(AuthorizationScope.ForDossier(tenant.Current.DossierId), includeClosed, queries, ct))
            .RequirePermission(Permission.AlertesLire)
            .WithName("ListDossierAlerts")
            .Produces<AlertCentreResponse>();

        group.MapGet("/{id:guid}", (Guid id, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                GetAsync(id, AuthorizationScope.ForDossier(tenant.Current.DossierId), queries, ct))
            .RequirePermission(Permission.AlertesLire)
            .WithName("GetDossierAlert")
            .Produces<AlertDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Routes du cabinet (<c>/api/v1/cabinets/{cabinetId}/alerts</c>) : lecture et traitement.</summary>
    public static RouteGroupBuilder MapCabinetAlerts(this RouteGroupBuilder cabinet)
    {
        RouteGroupBuilder group = cabinet.MapGroup("/alerts").WithTags("Alerts");

        group.MapGet("/", (Guid cabinetId, IQueryDispatcher queries, CancellationToken ct, bool includeClosed = false) =>
                ListAsync(Cabinet(cabinetId), includeClosed, queries, ct))
            .RequireCabinetPermission(Permission.AlertesLire)
            .WithName("ListCabinetAlerts")
            .Produces<AlertCentreResponse>();

        group.MapGet("/{id:guid}", (Guid cabinetId, Guid id, IQueryDispatcher queries, CancellationToken ct) =>
                GetAsync(id, Cabinet(cabinetId), queries, ct))
            .RequireCabinetPermission(Permission.AlertesLire)
            .WithName("GetCabinetAlert")
            .Produces<AlertDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/acknowledge", (Guid cabinetId, Guid id, ICommandDispatcher commands, CancellationToken ct) =>
                ActAsync(new AcknowledgeAlertCommand(new AlertId(id), Cabinet(cabinetId)), commands, ct))
            .RequireCabinetPermission(Permission.AlertesTraiter)
            .WithName("AcknowledgeAlert")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/resolve", (Guid cabinetId, Guid id, ICommandDispatcher commands, CancellationToken ct) =>
                ActAsync(new ResolveAlertCommand(new AlertId(id), Cabinet(cabinetId)), commands, ct))
            .RequireCabinetPermission(Permission.AlertesTraiter)
            .WithName("ResolveAlert")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/severity", (Guid cabinetId, Guid id, ChangeAlertSeverityRequest request, ICommandDispatcher commands, CancellationToken ct) =>
                ActAsync(new ChangeAlertSeverityCommand(new AlertId(id), (Severity)request.Severity, request.Reason, Cabinet(cabinetId)), commands, ct))
            .RequireCabinetPermission(Permission.AlertesTraiter)
            .WithName("ChangeAlertSeverity")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static AuthorizationScope Cabinet(Guid cabinetId) => AuthorizationScope.ForCabinet(new OrganisationId(cabinetId));

    private static async Task<IResult> ListAsync(AuthorizationScope within, bool includeClosed, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AlertCentreResponse> centre = await queries.QueryAsync(new ListAlertsQuery(within, includeClosed), cancellationToken).ConfigureAwait(false);
        return centre.IsSuccess ? TypedResults.Ok(centre.Value) : centre.Errors.ToProblem();
    }

    private static async Task<IResult> GetAsync(Guid id, AuthorizationScope within, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AlertDetailResponse> detail = await queries.QueryAsync(new GetAlertQuery(new AlertId(id), within), cancellationToken).ConfigureAwait(false);
        return detail.IsSuccess ? TypedResults.Ok(detail.Value) : detail.Errors.ToProblem();
    }

    private static async Task<IResult> ActAsync(AlertActionCommand command, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result result = command switch
        {
            AcknowledgeAlertCommand acknowledge => await commands.SendAsync(acknowledge, cancellationToken).ConfigureAwait(false),
            ResolveAlertCommand resolve => await commands.SendAsync(resolve, cancellationToken).ConfigureAwait(false),
            ChangeAlertSeverityCommand change => await commands.SendAsync(change, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        return result.IsSuccess ? TypedResults.NoContent() : result.Errors.ToProblem();
    }
}
