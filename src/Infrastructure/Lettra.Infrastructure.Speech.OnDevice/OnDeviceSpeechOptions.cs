namespace Lettra.Infrastructure.Speech.OnDevice;

/// <summary>Modèles Whisper multilingues proposés sur le poste : plus gros, plus juste, plus lent.</summary>
public enum WhisperModelSize
{
    /// <summary>75 Mio : téléphones modestes, tests de CI.</summary>
    Tiny = 1,

    /// <summary>142 Mio : choix par défaut, bon compromis sur ordinateur et téléphone récent.</summary>
    Base = 2,

    /// <summary>466 Mio : ordinateurs, meilleure justesse sur les noms propres et les nombres.</summary>
    Small = 3,
}

/// <summary>Réglages de la voix sur le poste (MAUI). L'hôte les fixe : dossier de l'application, modèle, langues.</summary>
public sealed class OnDeviceSpeechOptions
{
    /// <summary>Réglage du moteur sur le poste, même clé que les serveurs (<c>Lettra:Speech:Engine</c>).</summary>
    public const string EngineKey = "Lettra:Speech:Engine";

    /// <summary>
    /// La dictée sur le poste est-elle ouverte, d'après <c>Lettra:Speech:Engine</c> ? Sans réglage, la valeur de la
    /// version construite (<paramref name="enabledByDefault"/>) ; <c>OnDevice</c> l'ouvre ; <c>None</c> la ferme, comme
    /// toute autre valeur (le poste n'a pas d'autre moteur que Whisper.net : un réglage incompris ne l'ouvre pas).
    /// </summary>
    public static bool IsEnabled(string? engine, bool enabledByDefault = true) =>
        string.IsNullOrWhiteSpace(engine)
            ? enabledByDefault
            : string.Equals(engine.Trim(), "OnDevice", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dossier où le modèle est rangé (<c>FileSystem.AppDataDirectory/voix</c> sur MAUI).</summary>
    public required string ModelDirectory { get; init; }

    public WhisperModelSize Model { get; init; } = WhisperModelSize.Base;

    /// <summary>
    /// Adresse de téléchargement des modèles ggml (dépôt public de whisper.cpp). Un cabinet peut la remplacer par son
    /// propre serveur ; le fichier est vérifié par empreinte quelle que soit sa source.
    /// </summary>
    public Uri DownloadBaseAddress { get; init; } = new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/");

    /// <summary>Faux pour un poste sans aucun accès réseau : le modèle doit alors être déposé dans <see cref="ModelDirectory"/>.</summary>
    public bool AllowDownload { get; init; } = true;

    public IReadOnlyList<string> Languages { get; init; } = ["fr", "en"];

    public int MaxSeconds { get; init; } = 30;

    /// <summary>Fils de calcul : la moitié des cœurs, pour laisser l'interface fluide pendant la transcription.</summary>
    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);
}
