using System.Text.Json;
using Lettra.Infrastructure.Speech.OnDevice;
using Microsoft.Maui.Storage;

namespace Lettra.App.Maui;

/// <summary>
/// Dictée sur le poste (Whisper.net, hors ligne) : ouverte par défaut, y compris dans la version de démonstration. Elle
/// se ferme de deux façons :
/// <list type="bullet">
/// <item>à la construction, <c>-p:LettraDictee=false</c> (Lettra.App.Maui.csproj) : fermée par défaut sur ce build ;</item>
/// <item>sur un poste, dans <c>lettra.json</c> (dossier de données de l'application, le fichier du poste relié) :
/// <code>{ "Lettra": { "Speech": { "Engine": "None" } } }</code> ; <c>"OnDevice"</c> la rouvre.</item>
/// </list>
/// Fermée, le micro n'est jamais ouvert et le modèle n'est pas téléchargé ; le bouton micro n'apparaît pas.
/// </summary>
internal static class DeviceSpeech
{
#if LETTRA_SANS_DICTEE
    private const bool EnabledByDefault = false;
#else
    private const bool EnabledByDefault = true;
#endif

    public static bool IsEnabled() =>
        OnDeviceSpeechOptions.IsEnabled(Engine(Path.Combine(FileSystem.AppDataDirectory, ConnectedDevice.SettingsFile)), EnabledByDefault);

    /// <summary>Lettra:Speech:Engine de <c>lettra.json</c>, ou <c>null</c> sans fichier ni réglage.</summary>
    private static string? Engine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("Lettra", out JsonElement lettra)
               && lettra.TryGetProperty("Speech", out JsonElement speech)
               && speech.TryGetProperty("Engine", out JsonElement engine)
               && engine.ValueKind == JsonValueKind.String
            ? engine.GetString()
            : null;
    }
}
