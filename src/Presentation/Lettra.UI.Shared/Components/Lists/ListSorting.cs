using System.Globalization;

namespace Lettra.UI.Shared.Components.Lists;

/// <summary>
/// Tri à l'écran des listes chargées en entier (quelques dizaines de lignes : alertes, dossiers du cabinet, comptes).
/// Les listes paginées trient sur le serveur. Les valeurs absentes restent en fin de liste dans les deux sens, les textes
/// se comparent comme un humain les lit (sans casse ni accents, « Élodie » avec « Elodie »), et l'ordre d'origine départage.
/// </summary>
public static class ListSorting
{
    public static IReadOnlyList<T> Sort<T, TColumn>(IEnumerable<T> items, SortState<TColumn> state, IReadOnlyDictionary<TColumn, Func<T, object?>> keys)
        where TColumn : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(keys);
        if (!keys.TryGetValue(state.Column, out Func<T, object?>? key))
        {
            return [.. items];
        }

        ValueComparer comparer = new(state.Descending);
        return [.. items.OrderBy(key, comparer)];
    }

    /// <summary>Compare deux valeurs d'une même colonne ; le sens ne s'applique qu'aux valeurs présentes.</summary>
    private sealed class ValueComparer(bool descending) : IComparer<object?>
    {
        private static readonly CompareInfo Text = CultureInfo.CurrentCulture.CompareInfo;

        public int Compare(object? x, object? y)
        {
            if (x is null || y is null)
            {
                return (x is null ? 1 : 0) - (y is null ? 1 : 0);
            }

            int order = x is string a && y is string b
                ? Text.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)
                : Comparer<object>.Default.Compare(x, y);
            return descending ? -order : order;
        }
    }
}
