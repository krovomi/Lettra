using Lettra.Application.Abstractions.Speech;
using Lettra.Speech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Infrastructure.Speech.OnDevice;

public static class DependencyInjection
{
    /// <summary>
    /// Voix sur le poste (MAUI, modes 1 et 4) : Whisper.net remplace le moteur par défaut (désactivé). À appeler par
    /// l'hôte après <c>AddLettra</c>, avec le dossier de données de l'application.
    /// </summary>
    public static IServiceCollection AddOnDeviceSpeech(this IServiceCollection services, OnDeviceSpeechOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string[] languages = [.. options.Languages.Select(code => SpokenLanguages.FromCode(code)?.ToCode()).OfType<string>().Distinct(StringComparer.Ordinal)];
        int maxSeconds = Math.Clamp(options.MaxSeconds, 1, SpeechSettings.HardMaxSeconds);

        services.AddHttpClient(nameof(WhisperModelStore), client => client.Timeout = TimeSpan.FromMinutes(15));
        services.AddSingleton(options);
        services.AddSingleton<WhisperModelStore>();
        services.Replace(ServiceDescriptor.Singleton(new SpeechSettings(languages.Length > 0, WhisperNetSpeechToText.EngineName, OnDevice: true, languages, maxSeconds)));
        services.Replace(ServiceDescriptor.Singleton<ISpeechToText, WhisperNetSpeechToText>());
        return services;
    }
}
