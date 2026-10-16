using Lettra.Application.Features.Speech;
using Lettra.Contracts.Common;
using Lettra.Contracts.Speech;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.InProcess;

/// <summary>Même cas d'usage que la route <c>/voix</c> de l'API, sur le poste : avec Whisper.net, l'audio ne quitte pas l'appareil.</summary>
internal sealed class InProcessSpeechGateway(IServiceScopeFactory scopes) : ISpeechGateway
{
    public Task<OperationResult<SpeechStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(scopes, (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new GetSpeechStatusQuery(dossier), ct), cancellationToken);

    public Task<OperationResult<TranscriptionResponse>> TranscribeAsync(
        ReadOnlyMemory<byte> wav, SpeechMode mode, string language, CancellationToken cancellationToken) =>
        InProcessScope.RunAsync(
            scopes,
            (sp, dossier, ct) => InProcessScope.QueryAsync(sp, new TranscribeSpeechQuery(dossier, wav, language, mode), ct),
            cancellationToken);
}
