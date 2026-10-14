namespace Lettra.Calculations;

/// <summary>
/// Passage d'un prix unitaire HT à TTC et retour, pour l'affichage et la saisie du catalogue.
/// Partagé serveur ↔ WASM : l'écran montre exactement le prix que le serveur retiendra.
/// Un document ne calcule jamais sa TVA à partir de ces prix TTC : la ventilation par taux de
/// <see cref="DocumentCalculator"/> reste la seule référence.
/// </summary>
public static class PriceConversion
{
    /// <summary>Prix TTC affiché pour un prix HT, arrondi au centime.</summary>
    public static decimal InclTax(decimal unitPriceExclTax, decimal vatRatePercent) =>
        Rounding.ToCents(unitPriceExclTax * (100m + EnsureRate(vatRatePercent)) / 100m);

    /// <summary>
    /// Prix HT (4 décimales) qui redonne le prix TTC saisi : on part du quotient exact, puis on garde la
    /// valeur à 4 décimales la plus proche dont le TTC arrondi au centime retombe sur la saisie.
    /// </summary>
    public static decimal ExclTaxFromInclTax(decimal unitPriceInclTax, decimal vatRatePercent)
    {
        decimal factor = (100m + EnsureRate(vatRatePercent)) / 100m;
        decimal candidate = Rounding.ToStorage(unitPriceInclTax / factor);
        decimal target = Rounding.ToCents(unitPriceInclTax);
        const decimal step = 0.0001m;

        for (int i = 0; i < 100 && InclTax(candidate, vatRatePercent) != target; i++)
        {
            candidate += InclTax(candidate, vatRatePercent) < target ? step : -step;
        }

        return candidate;
    }

    private static decimal EnsureRate(decimal vatRatePercent) =>
        vatRatePercent is < 0m or > 100m
            ? throw new ArgumentOutOfRangeException(nameof(vatRatePercent), vatRatePercent, "Le taux de TVA doit être compris entre 0 et 100 %.")
            : vatRatePercent;
}
