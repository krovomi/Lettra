namespace Lettra.Poc.Grille.Ui.Clavier;

/// <summary>Densité d'affichage (annexe A §4.1) : mêmes composants, variables CSS différentes.</summary>
public enum UiDensity { Confort, Expert }

public static class UiDensityExtensions
{
    public static string CssClass(this UiDensity density) => density == UiDensity.Expert ? "density-expert" : "density-confort";

    /// <summary>Hauteur de ligne en pixels, transmise à Virtualize comme estimation initiale.</summary>
    public static float RowHeight(this UiDensity density) => density == UiDensity.Expert ? 24f : 40f;
}
