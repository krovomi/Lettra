namespace Lettra.Calculations;

/// <summary>
/// Catégories de TVA de la norme EN 16931 (liste UNTDID 5305, champs BT-118 et BT-151), telles que Factur-X
/// et UBL les sérialisent. Seules <see cref="Standard"/> et <see cref="ZeroRated"/> portent un taux.
/// </summary>
public static class VatCategoryCodes
{
    /// <summary>Taux normal, intermédiaire ou réduit.</summary>
    public const string Standard = "S";

    /// <summary>Taux zéro.</summary>
    public const string ZeroRated = "Z";

    /// <summary>Exonérée (avec un motif VATEX).</summary>
    public const string Exempt = "E";

    /// <summary>Autoliquidation par le preneur.</summary>
    public const string ReverseCharge = "AE";

    /// <summary>Livraison intracommunautaire exonérée.</summary>
    public const string IntraCommunity = "K";

    /// <summary>Exportation hors de l'UE.</summary>
    public const string Export = "G";

    /// <summary>Hors du champ de la TVA (franchise en base).</summary>
    public const string OutOfScope = "O";

    public static IReadOnlyList<string> All { get; } = [Standard, ZeroRated, Exempt, ReverseCharge, IntraCommunity, Export, OutOfScope];

    /// <summary>Vrai si la catégorie applique un taux (S, Z) ; les autres ont une TVA nulle.</summary>
    public static bool IsTaxed(string? category) => category is null or Standard or ZeroRated;
}
