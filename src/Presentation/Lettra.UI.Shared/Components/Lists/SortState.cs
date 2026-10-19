using Lettra.Contracts.Common;

namespace Lettra.UI.Shared.Components.Lists;

/// <summary>Une colonne triable d'une liste : son libellé et le sens du premier clic (dates et montants : décroissant).</summary>
/// <typeparam name="TColumn">Colonnes de la liste, une énumération (celle du contrat quand le tri se fait sur le serveur).</typeparam>
public sealed record SortOption<TColumn>(TColumn Column, string Label, SortDirectionDto FirstDirection = SortDirectionDto.Ascending)
    where TColumn : struct, Enum;

/// <summary>
/// Tri courant d'une liste. Un clic sur la colonne déjà triée inverse le sens ; un clic sur une autre colonne la trie
/// dans son sens naturel (<see cref="SortOption{TColumn}.FirstDirection"/>).
/// </summary>
public sealed class SortState<TColumn>(TColumn column, SortDirectionDto direction = SortDirectionDto.Ascending)
    where TColumn : struct, Enum
{
    private readonly TColumn _initialColumn = column;
    private readonly SortDirectionDto _initialDirection = direction;

    public TColumn Column { get; private set; } = column;

    public SortDirectionDto Direction { get; private set; } = direction;

    public bool Descending => Direction == SortDirectionDto.Descending;

    /// <summary>Le tri a quitté celui de l'ouverture de l'écran.</summary>
    public bool IsChanged => !EqualityComparer<TColumn>.Default.Equals(Column, _initialColumn) || Direction != _initialDirection;

    public void Toggle(TColumn column, SortDirectionDto firstDirection = SortDirectionDto.Ascending)
    {
        if (EqualityComparer<TColumn>.Default.Equals(Column, column))
        {
            Direction = Descending ? SortDirectionDto.Ascending : SortDirectionDto.Descending;
            return;
        }

        Set(column, firstDirection);
    }

    public void Set(TColumn column, SortDirectionDto direction)
    {
        Column = column;
        Direction = direction;
    }

    public void Reset() => Set(_initialColumn, _initialDirection);

    /// <summary>Valeur de <c>aria-sort</c> de l'en-tête : les lecteurs d'écran annoncent la colonne triée et son sens.</summary>
    public string AriaSort(TColumn column) =>
        !EqualityComparer<TColumn>.Default.Equals(Column, column) ? "none" : Descending ? "descending" : "ascending";
}
