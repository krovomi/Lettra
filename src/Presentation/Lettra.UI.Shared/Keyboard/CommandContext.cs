namespace Lettra.UI.Shared.Keyboard;

/// <summary>Cellule active au moment d'une commande (ligne, clé de colonne). <see cref="None"/> hors grille.</summary>
public sealed record CommandContext(int Row, string Column)
{
    public static readonly CommandContext None = new(-1, string.Empty);
}

/// <summary>Raccourci déclaré dans une portée. Le libellé est déjà traduit par la page qui le déclare.</summary>
public sealed record ShortcutBinding(
    KeyChord Chord,
    string Label,
    Func<CommandContext, Task> Execute,
    Func<CommandContext, bool>? CanExecute = null,
    bool ShowInBar = true);
