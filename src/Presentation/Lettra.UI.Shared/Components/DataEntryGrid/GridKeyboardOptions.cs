namespace Lettra.UI.Shared.Components.DataEntryGrid;

/// <summary>Options passées à <c>attachGrid</c> : touches de commande remontées à .NET, colonnes saisissables dans l'ordre.</summary>
public sealed record GridKeyboardOptions(IReadOnlyList<string> CommandKeys, IReadOnlyList<int> EditableColumns);
