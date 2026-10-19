using System.Globalization;

namespace Lettra.Calculations.Compliance;

/// <summary>
/// Textes des mentions légales imprimées en pied de pièce, et leur reconnaissance par le contrôle. La pièce validée fige
/// ses mentions en texte : le contrôle les relit donc en texte, et la reconnaissance reste tolérante pour les pièces
/// figées par une version précédente. Le texte écrit et le texte reconnu sont définis ici, au même endroit : reformuler
/// une mention sans adapter sa reconnaissance casse <c>LegalMentionTextsTests</c>.
/// </summary>
public static class LegalMentionTexts
{
    /// <summary>Option pour le paiement de la TVA d'après les débits (art. 269, 2-a CGI).</summary>
    public const string VatOnDebits = "Option pour le paiement de la TVA d'après les débits";

    /// <summary>Indemnité forfaitaire pour frais de recouvrement due par un client professionnel (art. D441-5 C.com).</summary>
    public static string RecoveryIndemnity { get; } = string.Create(
        CultureInfo.GetCultureInfo("fr-FR"),
        $"Indemnité forfaitaire pour frais de recouvrement en cas de retard de paiement : {MandatoryMentions.RecoveryIndemnityAmount:0} €");

    /// <summary>Vrai si la mention est l'option pour la TVA d'après les débits.</summary>
    public static bool IsVatOnDebits(string mention)
    {
        ArgumentNullException.ThrowIfNull(mention);
        return mention.Contains("débits", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Vrai si la mention cite le texte légal d'une exonération, d'une autoliquidation ou de la franchise.</summary>
    public static bool IsVatLegalText(string mention)
    {
        ArgumentNullException.ThrowIfNull(mention);
        return mention.Contains("CGI", StringComparison.Ordinal) || mention.Contains("Code général des impôts", StringComparison.Ordinal);
    }

    /// <summary>Vrai si la mention est l'indemnité forfaitaire de recouvrement.</summary>
    public static bool IsRecoveryIndemnity(string mention)
    {
        ArgumentNullException.ThrowIfNull(mention);
        return mention.Contains("40", StringComparison.Ordinal) && mention.Contains("recouvrement", StringComparison.OrdinalIgnoreCase);
    }
}
