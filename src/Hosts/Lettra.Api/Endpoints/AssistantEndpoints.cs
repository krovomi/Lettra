using System.Text.Json;
using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Assistant;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Common;

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
            .Produces<AssistantEvent>(StatusCodes.Status200OK, NdjsonContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/journal", JournalAsync)
            .WithName("GetAssistantJournal")
            .Produces<IReadOnlyList<AssistantJournalEntry>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
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

    private static async Task<IResult> JournalAsync(
        ITenantContext tenant,
        IQueryDispatcher queries,
        CancellationToken cancellationToken,
        int limit = 200)
    {
        Result<IReadOnlyList<AssistantJournalEntry>> result = await queries
            .QueryAsync(new GetAssistantJournalQuery(tenant.Current.DossierId, limit), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Errors.ToProblem();
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
