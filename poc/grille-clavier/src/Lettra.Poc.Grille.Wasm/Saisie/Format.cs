using System.Globalization;

namespace Lettra.Poc.Grille.Wasm.Saisie;

/// <summary>Affichage et saisie des nombres au format français (1 234,56), point accepté à la saisie.</summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Montant(decimal value) => value.ToString("N2", Fr);
    public static string Quantite(decimal value) => value.ToString("#,##0.###", Fr);
    public static string Taux(decimal value) => value.ToString("0.##", Fr);

    public static decimal Nombre(string text)
    {
        var compact = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.');
        if (compact.Length == 0) return 0m;
        if (!decimal.TryParse(compact, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Nombre invalide : '{text}'.");
        return value;
    }
}
