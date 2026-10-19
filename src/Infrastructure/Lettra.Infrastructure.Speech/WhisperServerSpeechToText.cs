using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Features.Speech;
using Lettra.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Speech;

/// <summary>
/// Transcription par le serveur whisper.cpp de la cellule (<c>POST /inference</c>, multipart). Le texte reçu n'est
/// pas journalisé ; un échec ne journalise que son type et le statut HTTP.
/// </summary>
internal sealed partial class WhisperServerSpeechToText(HttpClient http, WhisperServerOptions options, ILogger<WhisperServerSpeechToText> logger)
    : ISpeechToText
{
    public const string EngineName = "whisper-serveur";

    /// <summary>Délai de la vérification d'état : au-delà, le moteur est dit « en préparation ».</summary>
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public string Name => EngineName;

    public async Task<Result<SpeechTranscript>> TranscribeAsync(SpeechAudio audio, SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(request);
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(audio.Wav.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "dictee.wav");
        form.Add(new StringContent(request.Language), "language");
        form.Add(new StringContent(request.Prompt), "prompt");
        form.Add(new StringContent("0.0"), "temperature");
        form.Add(new StringContent("json"), "response_format");

        try
        {
            using HttpResponseMessage response = await http
                .PostAsync(new Uri(options.InferencePath, UriKind.Relative), form, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, (int)response.StatusCode);
                return SpeechErrors.EngineUnavailable;
            }

            InferenceResponse? body = await response.Content
                .ReadFromJsonAsync(WhisperJsonContext.Default.InferenceResponse, cancellationToken).ConfigureAwait(false);
            return body?.Text is null ? SpeechErrors.EngineUnavailable : new SpeechTranscript(body.Text);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                          && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, exception.GetType().Name);
            return SpeechErrors.EngineUnavailable;
        }
    }

    /// <summary>
    /// Le serveur répond 200 sur sa racine une fois le modèle chargé ; pendant le téléchargement il n'écoute pas
    /// encore, et pendant le chargement il répond 503. Réponse courte : l'écran d'état n'attend pas plus.
    /// </summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using HttpResponseMessage response = await http
                .GetAsync(new Uri(string.Empty, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Warning, Message = "Serveur Whisper : réponse {StatusCode}")]
    private static partial void LogRefused(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Serveur Whisper injoignable : {ExceptionType}")]
    private static partial void LogUnreachable(ILogger logger, string exceptionType);

    internal sealed record InferenceResponse([property: JsonPropertyName("text")] string? Text);
}

[JsonSerializable(typeof(WhisperServerSpeechToText.InferenceResponse))]
internal sealed partial class WhisperJsonContext : JsonSerializerContext;

/// <summary>Garde-fou d'ADR-0012 : l'audio d'un dossier ne part que vers un serveur de la cellule ou du cabinet.</summary>
internal static class InternalEndpoint
{
    private static readonly string[] InternalSuffixes = [".svc", ".cluster.local", ".internal", ".local", ".lan", ".home.arpa", ".localhost"];

    public static bool IsInternal(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        string host = endpoint.IdnHost.TrimEnd('.');
        if (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? address))
        {
            return IsPrivate(address);
        }

        return !host.Contains('.', StringComparison.Ordinal)
               || InternalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
               || host.Contains(".svc.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivate(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (adresses locales uniques) et fe80::/10 (lien local).
            byte first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        }

        byte[] b = address.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // 100.64.0.0/10 (réseaux d'opérateur, Tailscale)
    }

    public static string Describe(Uri endpoint) => string.Create(CultureInfo.InvariantCulture, $"{endpoint.Scheme}://{endpoint.IdnHost}:{endpoint.Port}");
}
