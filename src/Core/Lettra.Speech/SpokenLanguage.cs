namespace Lettra.Speech;

/// <summary>Langues de la dictée : celles de l'interface (français par défaut, anglais).</summary>
public enum SpokenLanguage
{
    French = 1,
    English = 2,
}

public static class SpokenLanguages
{
    /// <summary>Codes ISO 639-1 acceptés, dans l'ordre de préférence.</summary>
    public static IReadOnlyList<string> Codes { get; } = ["fr", "en"];

    /// <summary>Langue d'un code ISO 639-1 ou d'une culture (<c>fr</c>, <c>fr-FR</c>, <c>en-GB</c>) ; <c>null</c> si elle n'est pas prise en charge.</summary>
    public static SpokenLanguage? FromCode(string? code)
    {
        string prefix = (code ?? string.Empty).Split('-', '_')[0].Trim().ToUpperInvariant();
        return prefix switch
        {
            "FR" => SpokenLanguage.French,
            "EN" => SpokenLanguage.English,
            _ => null,
        };
    }

    public static string ToCode(this SpokenLanguage language) => language == SpokenLanguage.English ? "en" : "fr";
}
