using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Insights;
using Lettra.Domain.Common;
using Lettra.Exports.Application.Insights.Prevention;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Radar de prévention des difficultés, dans le dossier ouvert (en-tête <c>X-Dossier</c>) : niveau d'attention et
/// signaux, carte de l'accueil, seuils du dossier, courrier type de proposition de rendez-vous. Réservé au cabinet
/// (<c>PreventionConsulter</c>, <c>PreventionRegler</c> pour les seuils) ; règles en C#, aucune IA.
/// </summary>
internal static class PreventionEndpoints
{
    public static RouteGroupBuilder MapPrevention(this RouteGroupBuilder insights)
    {
        RouteGroupBuilder group = insights.MapGroup("/prevention").WithTags("Prevention");

        group.MapGet("", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPreventionRadarQuery(tenant.Current.DossierId), ct))
            .RequirePermission(Permission.PreventionConsulter)
            .WithName("GetPreventionRadar")
            .Produces<PreventionRadarResponse>();

        // L'accueil du dossier s'ouvre pour tous : sans le droit, la carte est cachée (200), jamais un refus.
        group.MapGet("/summary", (ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPreventionSummaryQuery(tenant.Current.DossierId), ct))
            .FilterByPermission(Permission.PreventionConsulter)
            .WithName("GetPreventionSummary")
            .Produces<PreventionSummaryResponse>();

        group.MapPut("/settings", (UpdatePreventionSettingsRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken ct) =>
                SendAsync(commands, new UpdatePreventionSettingsCommand(tenant.Current.DossierId, request), ct))
            .RequirePermission(Permission.PreventionRegler)
            .WithName("UpdatePreventionSettings")
            .Produces<PreventionSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapGet("/letter", (string? language, ITenantContext tenant, IQueryDispatcher queries, CancellationToken ct) =>
                QueryAsync(queries, new GetPreventionLetterQuery(tenant.Current.DossierId, language), ct))
            .RequirePermission(Permission.PreventionConsulter)
            .WithName("GetPreventionLetter")
            .Produces<PreventionLetterResponse>();

        group.MapPost("/letter", SendLetterAsync)
            .RequirePermission(Permission.PreventionConsulter)
            .WithName("SendPreventionLetter")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> SendLetterAsync(
        SendPreventionLetterRequest request, ITenantContext tenant, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result sent = await commands.SendAsync(new SendPreventionLetterCommand(tenant.Current.DossierId, request), cancellationToken).ConfigureAwait(false);
        return NoContent(sent);
    }

    private static async Task<IResult> QueryAsync<T>(IQueryDispatcher queries, IQuery<Result<T>> query, CancellationToken cancellationToken) =>
        Ok(await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SendAsync<T>(ICommandDispatcher commands, ICommand<Result<T>> command, CancellationToken cancellationToken) =>
        Ok(await commands.SendAsync(command, cancellationToken).ConfigureAwait(false));
}
