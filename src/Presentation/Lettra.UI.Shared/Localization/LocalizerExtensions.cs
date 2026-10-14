using Microsoft.Extensions.Localization;

namespace Lettra.UI.Shared.Localization;

public static class LocalizerExtensions
{
    /// <summary>Traduit un code d'erreur ; un code sans traduction s'affiche tel quel (et fait échouer le test de couverture des ressources).</summary>
    public static string Error(this IStringLocalizer localizer, string code)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        LocalizedString text = localizer["Error." + code];
        return text.ResourceNotFound ? code : text.Value;
    }
}
