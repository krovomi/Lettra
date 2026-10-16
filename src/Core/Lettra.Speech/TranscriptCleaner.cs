using System.Text.RegularExpressions;

namespace Lettra.Speech;

/// <summary>
/// Retire de la transcription ce que Whisper invente sur du silence ou du bruit : mentions de sous-titrage apprises
/// sur des vidéos (« Sous-titres réalisés par la communauté d'Amara.org », « Merci d'avoir regardé »), didascalies
/// entre crochets ou parenthèses (« [Musique] », « (rires) »).
/// </summary>
public static partial class TranscriptCleaner
{
    private static readonly string[] Artifacts =
    [
        "sous-titres réalisés par la communauté d'amara.org",
        "sous-titrage st' 501",
        "sous-titrage société radio-canada",
        "sous-titrage fr",
        "merci d'avoir regardé cette vidéo",
        "merci d'avoir regardé",
        "abonnez-vous",
        "thanks for watching",
        "thank you for watching",
        "subtitles by the amara.org community",
    ];

    [GeneratedRegex(@"\[[^\]]*\]|\((?:musique|music|rires|laughter|applaudissements|applause|silence|bruit|noise|inaudible)[^)]*\)|\*[^*]*\*|♪[^♪]*♪?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Stage();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static string Clean(string? transcript)
    {
        string text = Stage().Replace(transcript ?? string.Empty, " ");
        foreach (string artifact in Artifacts)
        {
            int at;
            while ((at = text.IndexOf(artifact, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Remove(at, artifact.Length);
            }
        }

        text = Spaces().Replace(text, " ").Trim();
        // Ce qui reste n'est que de la ponctuation : rien n'a été dit.
        return text.Any(char.IsLetterOrDigit) ? text : string.Empty;
    }
}
