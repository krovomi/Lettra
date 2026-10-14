using Lettra.Domain.Common;

namespace Lettra.Domain.Catalog;

/// <summary>
/// Unité de vente, identifiée par son code de la recommandation n° 20 de la CEE-ONU : c'est le code
/// que Factur-X et UBL exigent dans les lignes de facture (<c>unitCode</c>). La liste est fermée ;
/// les libellés sont traduits par l'interface (ressource <c>Unit.&lt;code&gt;</c>).
/// </summary>
public sealed record UnitOfMeasure
{
    public static readonly UnitOfMeasure Piece = new("C62");
    public static readonly UnitOfMeasure Hour = new("HUR");
    public static readonly UnitOfMeasure Day = new("DAY");
    public static readonly UnitOfMeasure Month = new("MON");
    public static readonly UnitOfMeasure LumpSum = new("LS");
    public static readonly UnitOfMeasure Kilogram = new("KGM");
    public static readonly UnitOfMeasure Metre = new("MTR");
    public static readonly UnitOfMeasure SquareMetre = new("MTK");
    public static readonly UnitOfMeasure CubicMetre = new("MTQ");
    public static readonly UnitOfMeasure Litre = new("LTR");
    public static readonly UnitOfMeasure Kilometre = new("KMT");

    private static readonly Dictionary<string, UnitOfMeasure> Known =
        new[] { Piece, Hour, Day, Month, LumpSum, Kilogram, Metre, SquareMetre, CubicMetre, Litre, Kilometre }
            .ToDictionary(u => u.Code, StringComparer.Ordinal);

    private UnitOfMeasure(string code) => Code = code;

    public string Code { get; }

    /// <summary>Unités proposées, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<UnitOfMeasure> All { get; } = [Piece, Hour, Day, Month, LumpSum, Kilogram, Metre, SquareMetre, CubicMetre, Litre, Kilometre];

    public static Result<UnitOfMeasure> FromCode(string? code) =>
        code is not null && Known.TryGetValue(code.Trim().ToUpperInvariant(), out UnitOfMeasure? unit) ? unit : CatalogItemErrors.UnitUnknown;

    public override string ToString() => Code;
}
