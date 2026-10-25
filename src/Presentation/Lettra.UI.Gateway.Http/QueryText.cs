using System.Globalization;

namespace Lettra.UI.Gateway.Http;

/// <summary>Valeurs écrites dans l'adresse d'un appel : indépendantes de la langue de l'écran.</summary>
internal static class QueryText
{
    /// <summary>Date ISO (2026-10-19), comme l'API la lit.</summary>
    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
