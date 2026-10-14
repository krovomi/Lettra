using Lettra.UI.Shared.Keyboard;

namespace Lettra.UI.Shared.Components.DataEntryGrid;

/// <summary>
/// Colonne d'une grille de saisie. <see cref="Parse"/> transforme le texte saisi en nouvel élément
/// immuable (ou lève <see cref="FormatException"/>) ; sans Parse, la colonne est calculée et non saisissable.
/// </summary>
public sealed class GridColumn<TItem>
{
    /// <summary>Clé stable de la colonne, utilisée par les commandes (<see cref="CommandContext.Column"/>).</summary>
    public required string Key { get; init; }

    /// <summary>Libellé déjà traduit.</summary>
    public required string Label { get; init; }

    public required Func<TItem, string> Display { get; init; }

    public Func<TItem, string, TItem>? Parse { get; init; }

    /// <summary>Largeur CSS Grid de la colonne (ex. « 7rem », « 2fr »).</summary>
    public string Width { get; init; } = "1fr";

    public bool Numeric { get; init; }

    /// <summary>En disposition carte (écran étroit), la cellule occupe toute la largeur.</summary>
    public bool Wide { get; init; }

    public bool IsEditable => Parse is not null;
}

/// <summary>Saisie sur une ligne : ancienne et nouvelle valeur (mise à jour incrémentale, enregistrement).</summary>
public readonly record struct GridItemChange<TItem>(int Row, TItem Before, TItem After);

/// <summary>Cellule à focaliser après un appel .NET, renvoyée au module JS (index de colonne).</summary>
public sealed record FocusTarget(int Row, int Col);

/// <summary>Résultat de la validation d'une cellule : texte normalisé à afficher, ou ancien texte si refusé.</summary>
public sealed record CellCommitResult(string Text, bool Ok);

/// <summary>Accès à la cellule active, pour la barre de touches de fonction (clic ou toucher).</summary>
public interface IGridContext
{
    Task<CommandContext> GetActiveCellAsync();
}

/// <summary>Lignes actuellement rendues (virtualisation), pour mettre à jour une seule ligne après une saisie.</summary>
public interface IGridRowRegistry<TItem>
    where TItem : class
{
    void Attach(object key, GridRow<TItem> row);

    void Detach(object key, GridRow<TItem> row);
}
