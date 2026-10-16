using Lettra.Application.Abstractions.Speech;
using Lettra.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Lettra.Infrastructure.Speech;

public static class DependencyInjection
{
    /// <summary>
    /// Voix du déploiement (<c>Lettra:Speech</c>). Sans moteur, rien n'est enregistré et la voix reste désactivée.
    /// <c>WhisperServer</c> exige un point d'accès interne ; <c>OnDevice</c> ne fixe ici que les réglages, le moteur
    /// Whisper.net étant composé par l'hôte MAUI.
    /// </summary>
    public static IServiceCollection AddSpeech(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        SpeechOptions options = new();
        configuration.GetSection(SpeechOptions.SectionName).Bind(options);
        if (options.Engine == SpeechEngineKind.None)
        {
            return services;
        }

        services.Replace(ServiceDescriptor.Singleton(Settings(options, options.Engine == SpeechEngineKind.WhisperServer ? WhisperServerSpeechToText.EngineName : null)));
        if (options.Engine != SpeechEngineKind.WhisperServer)
        {
            return services;
        }

        WhisperServerOptions server = options.WhisperServer;
        Uri endpoint = server.Endpoint
            ?? throw new InvalidOperationException($"{SpeechOptions.SectionName}:WhisperServer:Endpoint est obligatoire avec le moteur WhisperServer.");
        if (!server.AllowPublicEndpoint && !InternalEndpoint.IsInternal(endpoint))
        {
            // Échec fermé, comme le moteur « Claude UE » sans point d'accès européen : l'audio ne sort pas de la cellule.
            throw new InvalidOperationException(
                $"{InternalEndpoint.Describe(endpoint)} n'est pas un point d'accès interne : la dictée ne part que vers un serveur Whisper de la cellule ou du cabinet (ADR-0012).");
        }

        services.AddHttpClient(nameof(WhisperServerSpeechToText), client =>
        {
            client.BaseAddress = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
            client.Timeout = server.Timeout;
        });
        services.Replace(ServiceDescriptor.Singleton<ISpeechToText>(sp => new WhisperServerSpeechToText(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WhisperServerSpeechToText)),
            server,
            sp.GetRequiredService<ILogger<WhisperServerSpeechToText>>())));
        return services;
    }

    /// <summary>Réglages publiés à l'interface : langues prises en charge seulement, durée bornée.</summary>
    public static SpeechSettings Settings(SpeechOptions options, string? engineName)
    {
        ArgumentNullException.ThrowIfNull(options);
        string[] languages = [.. (options.Languages.Count == 0 ? SpokenLanguages.Codes : (IEnumerable<string>)options.Languages)
            .Select(code => SpokenLanguages.FromCode(code)?.ToCode())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
        int maxSeconds = Math.Clamp(options.MaxSeconds, 1, SpeechSettings.HardMaxSeconds);
        return new SpeechSettings(languages.Length > 0, engineName, options.Engine == SpeechEngineKind.OnDevice, languages, maxSeconds);
    }
}
