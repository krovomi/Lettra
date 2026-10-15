namespace Lettra.UI.Shared.Localization;

/// <summary>
/// Marqueur des ressources partagées de l'UI, écrites par module dans <c>Modules/&lt;Module&gt;.resx</c> (français,
/// langue par défaut) et <c>Modules/&lt;Module&gt;.en.resx</c> (anglais), puis fusionnées à la compilation.
/// Les erreurs se traduisent par la clé <c>Error.&lt;code&gt;</c>.
/// </summary>
#pragma warning disable CA1052 // Type marqueur pour IStringLocalizer<T>, jamais instancié.
public sealed class SharedResource
#pragma warning restore CA1052
{
    private SharedResource()
    {
    }
}

/// <summary>Cultures prises en charge. Le français est la langue par défaut.</summary>
public static class LettraCultures
{
    public const string Default = "fr-FR";

    public static readonly IReadOnlyList<string> Supported = ["fr-FR", "en-US"];

    /// <summary>Choisit la culture : celle demandée si elle est prise en charge (même langue), sinon le français.</summary>
    public static System.Globalization.CultureInfo Resolve(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            string language = requested.Split('-')[0];
            string? match = Supported.FirstOrDefault(s => s.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return System.Globalization.CultureInfo.GetCultureInfo(match);
            }
        }

        return System.Globalization.CultureInfo.GetCultureInfo(Default);
    }

    /// <summary>Applique la culture à toute l'application (formats 1 234,56 € et 14/10/2026 en français).</summary>
    public static void Apply(System.Globalization.CultureInfo culture)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
    }
}
