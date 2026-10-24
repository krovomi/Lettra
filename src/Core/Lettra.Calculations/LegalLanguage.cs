using System.Globalization;

namespace Lettra.Calculations;

/// <summary>
/// Langue des mentions légales et des fichiers remis à l'administration, et langue des pièces quand le client n'en
/// demande pas d'autre : le français.
/// </summary>
public static class LegalLanguage
{
    public const string Name = "fr-FR";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(Name);
}
