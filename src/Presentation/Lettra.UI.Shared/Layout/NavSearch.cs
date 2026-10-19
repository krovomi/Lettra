using Lettra.UI.Shared.Components.Palette;

namespace Lettra.UI.Shared.Layout;

/// <summary>
/// Recherche du menu : sans accents ni casse, sur le libellé de l'écran puis sur ses mots-clés (« TVA » trouve
/// Déclarations). Un libellé qui correspond passe toujours devant un mot-clé ; à égalité, l'ordre du menu reste.
/// </summary>
public static class NavSearch
{
    private const int LabelBonus = 1000;

    public static IReadOnlyList<NavItem> Filter(IEnumerable<NavItem> items, string? query, Func<NavItem, string> label, Func<NavItem, string?> keywords)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(keywords);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [.. items];
        }

        return [.. items
            .Select((item, index) => (item, index, score: Score(label(item), keywords(item), query)))
            .Where(m => m.score > 0)
            .OrderByDescending(m => m.score)
            .ThenBy(m => m.index)
            .Select(m => m.item)];
    }

    public static int Score(string label, string? keywords, string query)
    {
        int byLabel = PaletteMatcher.Score(label, query);
        if (byLabel > 0)
        {
            return LabelBonus + byLabel;
        }

        return string.IsNullOrWhiteSpace(keywords) ? 0 : PaletteMatcher.Score(label + " " + keywords, query);
    }
}
