using Lettra.Api.Security;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Features.Preferences;
using Lettra.Contracts.Preferences;
using Lettra.Domain.Common;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Les préférences de l'utilisateur connecté (<c>/api/v1/me/preferences</c>), hors de tout dossier : chacun ne lit et
/// n'écrit que son propre compte, l'identifiant vient de la session. Sans base Catalog : 400 <c>Preferences.Unavailable</c>.
/// </summary>
internal static class UserPreferencesEndpoints
{
    public const string Path = "/api/v1/me/preferences";

    public static IEndpointRouteBuilder MapUserPreferences(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup(Path)
            .RequireAuthorization(LettraPolicies.User)
            .AddEndpointFilter<CsrfEndpointFilter>()
            .ForOwnAccountOnly()
            .WithTags("Preferences");

        group.MapGet("/", GetAsync)
            .WithName("GetUserPreferences")
            .Produces<UserPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/", SaveAsync)
            .WithName("SaveUserPreferences")
            .Produces<UserPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        group.MapDelete("/", ResetAsync)
            .WithName("ResetUserPreferences")
            .Produces<UserPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/usage", RecordUsageAsync)
            .WithName("RecordUsage")
            .Produces<UserPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/usage", ForgetUsageAsync)
            .WithName("ForgetUsage")
            .Produces<UserPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> GetAsync(IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> result = await queries.QueryAsync(new GetUserPreferencesQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> SaveAsync(SaveUserPreferencesRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> result = await commands.SendAsync(new SaveUserPreferencesCommand(request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> ResetAsync(ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> result = await commands.SendAsync(new ResetUserPreferencesCommand(), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> RecordUsageAsync(RecordUsageRequest request, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> result = await commands.SendAsync(new RecordUsageCommand(request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Sans <paramref name="dossierId"/> : les choix comptés de tous les dossiers.</summary>
    private static async Task<IResult> ForgetUsageAsync(Guid? dossierId, ICommandDispatcher commands, CancellationToken cancellationToken)
    {
        Result<UserPreferencesResponse> result = await commands.SendAsync(new ForgetUsageCommand(dossierId), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
