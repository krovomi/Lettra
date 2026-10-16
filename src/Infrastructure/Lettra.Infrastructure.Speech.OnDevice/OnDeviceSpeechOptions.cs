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
