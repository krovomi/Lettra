using System.Text;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Features.Speech;
using Lettra.Domain.Common;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace Lettra.Infrastructure.Speech.OnDevice;

/// <summary>
/// Transcription sur le poste par Whisper.net : rien ne quitte l'appareil, la dictée marche hors ligne. Le modèle est
/// chargé une fois ; une transcription à la fois, pour ne pas saturer un téléphone.
/// </summary>
internal sealed partial class WhisperNetSpeechToText(WhisperModelStore models, OnDeviceSpeechOptions options, ILogger<WhisperNetSpeechToText> logger)
    : ISpeechToText, IDisposable
{
    public const string EngineName = "whisper-poste";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;

    public string Name => EngineName;

    public async Task<Result<SpeechTranscript>> TranscribeAsync(SpeechAudio audio, SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(request);
        Result<string> model = await models.EnsureAsync(cancellationToken).ConfigureAwait(false);
        if (model.IsFailure)
        {
            return Result<SpeechTranscript>.Failure(model.Errors);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _factory ??= WhisperFactory.FromPath(model.Value);
            WhisperProcessor processor = _factory.CreateBuilder()
                .WithLanguage(request.Language)
                .WithPrompt(request.Prompt)
                .WithThreads(options.Threads)
                .WithNoContext()
                .Build();
            await using (processor.ConfigureAwait(false))
            {
                StringBuilder text = new();
                await foreach (SegmentData segment in processor.ProcessAsync(audio.ToSamples(), cancellationToken).ConfigureAwait(false))
                {
                    text.Append(segment.Text);
                }

                return new SpeechTranscript(text.ToString().Trim());
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Bibliothèque native absente pour cette plateforme, modèle illisible… : seul le type est journalisé.
            LogFailed(logger, exception.GetType().Name);
            return SpeechErrors.EngineUnavailable;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _gate.Dispose();
    }

    [LoggerMessage(EventId = 7202, Level = LogLevel.Error, Message = "Whisper sur le poste en échec : {ExceptionType}")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
