namespace Lettra.Contracts.Common;

/// <summary>
/// Le texte recherché d'une liste, tel que l'écran, l'API et les cas d'usage le transmettent : sans espaces autour,
/// absent quand il est vide. Les accents et la casse sont ignorés plus loin, par la base (colonne de recherche).
/// </summary>
public static class ListSearch
{
    public static string? Normalize(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
