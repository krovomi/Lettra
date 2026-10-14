namespace Lettra.Calculations;

/// <summary>
/// Règle d'arrondi unique de Lettra. Les montants sont portés en <see cref="decimal"/>
/// à 4 décimales en interne et arrondis au centime, au plus proche, les demis
/// s'éloignant de zéro (arrondi commercial : 0,125 € → 0,13 €).
/// </summary>
public static class Rounding
{
    public const int CentDecimals = 2;
    public const int StorageDecimals = 4;

    public static decimal ToCents(decimal amount) =>
        Math.Round(amount, CentDecimals, MidpointRounding.AwayFromZero);

    public static decimal ToStorage(decimal amount) =>
        Math.Round(amount, StorageDecimals, MidpointRounding.AwayFromZero);
}
