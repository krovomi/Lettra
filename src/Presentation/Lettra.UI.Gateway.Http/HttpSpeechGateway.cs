using System.Net.Http.Headers;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Speech;
using Lettra.UI.Abstractions;

namespace Lettra.UI.Gateway.Http;

/// <summary>Route <c>/voix</c> de l'API : le WAV part tel quel (<c>audio/wav</c>), la réponse est le texte mis en forme.</summary>
internal sealed class HttpSpeechGateway(HttpClient http) : ISpeechGateway
{
    public async Task<OperationResult<SpeechStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri("api/v1/voix/statut", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(ContractsJsonContext.Default.SpeechStatusResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<SpeechStatusResponse>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    public async Task<OperationResult<TranscriptionResponse>> TranscribeAsync(
        ReadOnlyMemory<byte> wav, SpeechMode mode, string language, CancellationToken cancellationToken)
    {
        try
        {
            using ReadOnlyMemoryContent content = new(wav);
            content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            Uri route = new($"api/v1/voix/transcription?mode={mode}&langue={Uri.EscapeDataString(language)}", UriKind.Relative);
            using HttpResponseMessage response = await http.PostAsync(route, content, cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(ContractsJsonContext.Default.TranscriptionResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (HttpCalls.IsUnreachable(e, cancellationToken))
        {
            return OperationResult<TranscriptionResponse>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}
