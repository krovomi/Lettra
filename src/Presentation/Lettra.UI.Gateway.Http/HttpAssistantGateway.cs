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
    public async Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("api/v1/assistant/statut", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(ContractsJsonContext.Default.AssistantStatusResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<AssistantStatusResponse>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

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
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AssistantEvent? assistantEvent = JsonSerializer.Deserialize(line, ContractsJsonContext.Default.AssistantEvent);
                if (assistantEvent is not null)
                {
                    yield return assistantEvent;
                }
            }
        }
    }
}
