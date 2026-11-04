using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Route <c>/assistant</c> de l'API : la réponse est lue ligne par ligne (<c>application/x-ndjson</c>) dès qu'elle arrive.</summary>
internal sealed class HttpAssistantGateway(HttpClient http) : IAssistantGateway
{
    public Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri("api/v1/assistant/statut", UriKind.Relative), ct),
            ContractsJsonContext.Default.AssistantStatusResponse,
            cancellationToken);

    public Task<OperationResult<IReadOnlyList<AssistantJournalEntry>>> GetJournalAsync(int limit, CancellationToken cancellationToken) =>
        HttpCalls.SendAsync(
            ct => http.GetAsync(new Uri(string.Create(CultureInfo.InvariantCulture, $"api/v1/assistant/journal?limit={limit}"), UriKind.Relative), ct),
            ContractsJsonContext.Default.IReadOnlyListAssistantJournalEntry,
            cancellationToken);

    public async IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        HttpResponseMessage? response = null;
        string? refusal = null;
        try
        {
            using HttpRequestMessage message = new(HttpMethod.Post, new Uri("api/v1/assistant", UriKind.Relative))
            {
                Content = JsonContent.Create(request, ContractsJsonContext.Default.AskAssistantRequest),
            };
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                OperationResult<AssistantStatusResponse> failure = await response
                    .ToOperationResultAsync(ContractsJsonContext.Default.AssistantStatusResponse, cancellationToken).ConfigureAwait(false);
                refusal = failure.Errors.Count > 0 ? failure.Errors[^1].Code : ErrorCodes.Api.Unexpected;
            }
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            refusal = ErrorCodes.Api.Unreachable;
        }

        using (response)
        {
            if (refusal is not null || response is null)
            {
                yield return new AssistantUnavailableEvent(refusal ?? ErrorCodes.Api.Unexpected);
                yield break;
            }

            Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using StreamReader reader = new(body);
            while (await ReadNextAsync(reader, cancellationToken).ConfigureAwait(false) is { } next)
            {
                yield return next;
                if (next is AssistantUnavailableEvent)
                {
                    yield break;
                }
            }
        }
    }

    /// <summary>
    /// L'événement suivant, ou <c>null</c> à la fin du flux. Un flux coupé en route (serveur arrêté, réseau perdu) ou une
    /// ligne illisible termine la réponse sur « serveur injoignable » : les étapes déjà reçues restent affichées.
    /// </summary>
    private static async Task<AssistantEvent?> ReadNextAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line) && JsonSerializer.Deserialize(line, ContractsJsonContext.Default.AssistantEvent) is { } assistantEvent)
                {
                    return assistantEvent;
                }
            }

            return null;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return new AssistantUnavailableEvent(ErrorCodes.Api.Unreachable);
        }
    }
}
