using Lettra.Poc.Grille.Calculs;
using Lettra.Poc.Grille.Ui.Grille;

namespace Lettra.Poc.Grille.Wasm.Saisie;

/// <summary>Colonnes d'une grille de lignes de facture. Les colonnes calculées n'ont pas de Parse.</summary>
public static class ColonnesFacture
{
    private static readonly HashSet<decimal> TauxAutorises = [0m, 2.1m, 5.5m, 10m, 20m];

    public static IReadOnlyList<GridColumn<LigneFacture>> Creer(IReadOnlyList<Article> catalogue)
    {
        var parCode = catalogue.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        return
        [
            new()
            {
                Key = "article", Label = "Article", Width = "6.5rem",
                Display = l => l.Article,
                // Code connu saisi directement : la ligne est complétée comme avec la liste de choix (F4).
                Parse = (l, t) => parCode.TryGetValue(t.Trim(), out var a) ? Appliquer(l, a) : l with { Article = t.Trim().ToUpperInvariant() },
            },
            new() { Key = "designation", Label = "Désignation", Width = "minmax(12rem, 3fr)", Wide = true,
                    Display = l => l.Designation, Parse = (l, t) => l with { Designation = t.Trim() } },
            new() { Key = "quantite", Label = "Qté", Width = "4.5rem", Numeric = true,
                    Display = l => Format.Quantite(l.Quantite), Parse = (l, t) => l with { Quantite = Format.Nombre(t) } },
            new() { Key = "unite", Label = "Unité", Width = "3.5rem",
                    Display = l => l.Unite, Parse = (l, t) => l with { Unite = t.Trim() } },
            new() { Key = "pu", Label = "PU HT", Width = "6.5rem", Numeric = true,
                    Display = l => Format.Montant(l.PrixUnitaireHt), Parse = (l, t) => l with { PrixUnitaireHt = Format.Nombre(t) } },
            new() { Key = "remise", Label = "Remise %", Width = "5rem", Numeric = true,
                    Display = l => Format.Taux(l.RemisePourcent), Parse = (l, t) => l with { RemisePourcent = Pourcentage(t) } },
            new() { Key = "tva", Label = "TVA %", Width = "4.5rem", Numeric = true,
                    Display = l => Format.Taux(l.TauxTva), Parse = (l, t) => l with { TauxTva = TauxTva(t) } },
            new() { Key = "ht", Label = "Montant HT", Width = "7.5rem", Numeric = true, Display = l => Format.Montant(l.MontantHt) },
            new() { Key = "ttc", Label = "Montant TTC", Width = "7.5rem", Numeric = true, Display = l => Format.Montant(l.MontantTtc) },
        ];
    }

    public static LigneFacture Appliquer(LigneFacture ligne, Article article) => ligne with
    {
        Article = article.Code,
        Designation = article.Designation,
        Unite = article.Unite,
        PrixUnitaireHt = article.PrixUnitaireHt,
        TauxTva = article.TauxTva,
    };

    private static decimal Pourcentage(string text)
    {
        var value = Format.Nombre(text);
        return value is >= 0m and <= 100m ? value : throw new FormatException("La remise doit être comprise entre 0 et 100 %.");
    }

    private static decimal TauxTva(string text)
    {
        var value = Format.Nombre(text);
        return TauxAutorises.Contains(value) ? value : throw new FormatException("Taux de TVA français attendu : 0, 2,1, 5,5, 10 ou 20.");
    }
}
