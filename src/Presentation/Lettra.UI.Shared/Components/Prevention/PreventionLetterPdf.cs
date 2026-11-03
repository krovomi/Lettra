using System.Globalization;
using Lettra.Printing;

namespace Lettra.UI.Shared.Components.Prevention;

/// <summary>
/// Le courrier de proposition de rendez-vous, tel que le collaborateur l'a relu, mis en lettre pour le PDF : le client en
/// destinataire, la date du jour dans la langue du courrier, l'objet en titre et un paragraphe par ligne de texte (les
/// lignes vides séparent les blocs ; une liste garde une ligne par sujet).
/// </summary>
public static class PreventionLetterPdf
{
    public static PrintableLetter Compose(string language, string company, string subject, string body, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        CultureInfo culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "fr-FR" : language);
        string[] paragraphs =
        [
            .. body.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];
        return new PrintableLetter(
            culture,
            [],
            string.IsNullOrWhiteSpace(company) ? [] : [company.Trim()],
            today.ToString("D", culture),
            subject.Trim(),
            [new LetterSection(string.Empty, paragraphs)],
            []);
    }

    /// <summary>Le nom du fichier : l'objet ne s'y retrouve pas, seulement la date (ISO, triable).</summary>
    public static string FileName(DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"rendez-vous-prevention-{today:yyyy-MM-dd}.pdf");
}
