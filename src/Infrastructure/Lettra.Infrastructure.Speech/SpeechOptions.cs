namespace Lettra.Infrastructure.Speech;

/// <summary>Moteurs vocaux possibles. Aucun service vocal tiers n'existe ici, volontairement (ADR-0012).</summary>
public enum SpeechEngineKind
{
    /// <summary>Voix désactivée (par défaut).</summary>
    None = 0,

    /// <summary>Serveur whisper.cpp dans la cellule (modes 3 et 5) ou sur le serveur du cabinet (mode 2).</summary>
    WhisperServer = 1,

    /// <summary>Whisper.net sur le poste (MAUI, modes 1 et 4) : composé par l'hôte avec <c>AddOnDeviceSpeech</c>.</summary>
    OnDevice = 2,
}

/// <summary>Section <c>Lettra:Speech</c>.</summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Lettra:Speech";

    public SpeechEngineKind Engine { get; set; } = SpeechEngineKind.None;

    /// <summary>Langues proposées (ISO 639-1), la première par défaut. Seules <c>fr</c> et <c>en</c> sont prises en charge.</summary>
    public IList<string> Languages { get; } = [];

    /// <summary>Durée maximale d'un enregistrement, en secondes (120 au plus).</summary>
    public int MaxSeconds { get; set; } = 30;

    public WhisperServerOptions WhisperServer { get; set; } = new();
}

public sealed class WhisperServerOptions
{
    /// <summary>Adresse du serveur whisper.cpp (<c>http://whisper:8080/</c>), dans la cellule.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Chemin d'inférence du serveur whisper.cpp.</summary>
    public string InferencePath { get; set; } = "inference";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Par défaut, seul un point d'accès interne est accepté : nom de service sans domaine, <c>*.svc</c>,
    /// <c>*.cluster.local</c>, <c>*.internal</c>, <c>*.local</c>, <c>*.lan</c>, ou adresse IP privée. Ce réglage ne se
    /// lève que pour un serveur Whisper du cabinet joint par un nom public, jamais pour un service tiers.
    /// </summary>
    public bool AllowPublicEndpoint { get; set; }
}
