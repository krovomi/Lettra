using System.Text.Json;
using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Assistant;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using static Lettra.Api.Infrastructure.HttpResults;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Assistant IA (annexe B §4.2). La réponse arrive au fil de l'eau : une ligne JSON par événement
/// (<c>application/x-ndjson</c>), pour que l'UI affiche les étapes et le texte dès qu'ils sont prêts.
/// </summary>
internal static class AssistantEndpoints
{
    public const string NdjsonContentType = "application/x-ndjson";

    public static RouteGroupBuilder MapAssistant(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/assistant").WithTags("Assistant");

        group.MapPost("/", AskAsync)
            .WithName("AskAssistant")
            .RequirePermission(Permission.IaUtiliser)
            .Produces<AssistantEvent>(StatusCodes.Status200OK, NdjsonContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/statut", StatusAsync)
            .WithName("GetAssistantStatus")
            .RequirePermission(Permission.IaUtiliser)
            .Produces<AssistantStatusResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/journal", JournalAsync)
            .WithName("GetAssistantJournal")
            .RequirePermission(Permission.IaUtiliser)
            .Produces<IReadOnlyList<AssistantJournalEntry>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    /// <summary>Réglages IA du cabinet (administrateur du cabinet), hors de tout dossier.</summary>
    public static RouteGroupBuilder MapAssistantSettings(this RouteGroupBuilder cabinet)
    {
        RouteGroupBuilder group = cabinet.MapGroup("/assistant/reglages").WithTags("Assistant");

        group.MapGet("/", GetSettingsAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("GetAssistantSettings")
            .Produces<AiSettingsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/", SaveSettingsAsync)
            .RequireCabinetPermission(Permission.CabinetAdministrer)
            .WithName("SaveAssistantSettings")
            .Produces<AiSettingsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<IResult> GetSettingsAsync(Guid cabinetId, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AiSettingsResponse> result = await queries
            .QueryAsync(new GetAiSettingsQuery(new OrganisationId(cabinetId)), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> SaveSettingsAsync(
        Guid cabinetId,
        SaveAiSettingsRequest request,
        ICommandDispatcher commands,
        CancellationToken cancellationToken)
    {
        Result<AiSettingsResponse> result = await commands
            .SendAsync(new SaveAiSettingsCommand(new OrganisationId(cabinetId), request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> AskAsync(
        AskAssistantRequest request,
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken)
    {
        AskAssistantQuery query = new(tenant.Current.DossierId, request.Feature, request.Question, request.OpenDocumentId);
        Result<IAsyncEnumerable<AssistantEvent>> result = await queries.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? new NdjsonStreamResult(result.Value) : result.Errors.ToProblem();
    }

    private static async Task<IResult> StatusAsync(ITenantContext tenant, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        Result<AssistantStatusResponse> result = await queries
            .QueryAsync(new GetAssistantStatusQuery(tenant.Current.DossierId), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    private static async Task<IResult> JournalAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        int limit = 200)
    {
        Result<IReadOnlyList<AssistantJournalEntry>> result = await queries
            .QueryAsync(new GetAssistantJournalQuery(tenant.Current.DossierId, limit), cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>Écrit chaque événement dès qu'il est produit, suivi d'un saut de ligne, et vide le tampon.</summary>
    private sealed class NdjsonStreamResult(IAsyncEnumerable<AssistantEvent> events) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            HttpResponse response = httpContext.Response;
            response.ContentType = NdjsonContentType;
            response.Headers.CacheControl = "no-store";
            CancellationToken aborted = httpContext.RequestAborted;
            await foreach (AssistantEvent assistantEvent in events.WithCancellation(aborted).ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(response.Body, assistantEvent, ContractsJsonContext.Default.AssistantEvent, aborted).ConfigureAwait(false);
                await response.Body.WriteAsync("\n"u8.ToArray(), aborted).ConfigureAwait(false);
                await response.Body.FlushAsync(aborted).ConfigureAwait(false);
            }
        }
    }
}
