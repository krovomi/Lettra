namespace Lettra.UI.Shared.Services;

/// <summary>Thème de l'interface (jetons de <c>lettra-tokens.css</c>), choisi dans l'en-tête et retenu sur l'appareil.</summary>
public enum UiTheme
{
    /// <summary>Clair ou sombre selon le système (défaut).</summary>
    System,
    Light,
    Dark,

    /// <summary>Ardoise : navigation bleu nuit, contenu clair et feutré.</summary>
    Slate,

    /// <summary>Contraste élevé (RGAA, WCAG AAA).</summary>
    Contrast,
}

public static class UiThemeExtensions
{
    /// <summary>Valeur de <c>data-theme</c> sur &lt;html&gt;, <c>null</c> pour suivre le système.</summary>
    public static string? DataTheme(this UiTheme theme) => theme switch
    {
        UiTheme.Light => "light",
        UiTheme.Dark => "dark",
        UiTheme.Slate => "slate",
        UiTheme.Contrast => "contrast",
        _ => null,
    };

    /// <summary>Icône Lucide du thème, reprise par le bouton de l'en-tête.</summary>
    public static string IconName(this UiTheme theme) => theme switch
    {
        UiTheme.Light => "sun",
        UiTheme.Dark => "moon",
        UiTheme.Slate => "palette",
        UiTheme.Contrast => "contrast",
        _ => "monitor",
    };
}
