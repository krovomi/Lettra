using Lettra.Poc.Grille.Calculs;

namespace Lettra.Poc.Grille.Wasm.Saisie;

/// <summary>Catalogue et lignes de démonstration, déterministes (graine fixe) pour des mesures reproductibles.</summary>
public static class DonneesDemo
{
    private static readonly string[] Prestations =
    [
        "Tenue comptable mensuelle", "Établissement du bilan", "Liasse fiscale", "Déclaration de TVA",
        "Bulletins de paie", "Conseil juridique", "Prévisionnel", "Audit contractuel", "Formation logiciel",
        "Assistance contrôle fiscal",
    ];

    private static readonly string[] Fournitures =
    [
        "Papier A4 80 g", "Classeur à levier", "Cartouche d'encre", "Clé USB 64 Go", "Agrafeuse",
        "Enveloppes C4", "Licence logiciel", "Écran 27 pouces", "Câble HDMI", "Casque audio",
    ];

    private static readonly decimal[] Taux = [20m, 20m, 20m, 10m, 5.5m, 2.1m, 0m];

    public static IReadOnlyList<Article> Catalogue { get; } = CreerCatalogue();

    private static List<Article> CreerCatalogue()
    {
        var random = new Random(7);
        var articles = new List<Article>();
        for (var i = 0; i < 200; i++)
        {
            var prestation = i % 2 == 0;
            var nom = prestation ? Prestations[i / 2 % Prestations.Length] : Fournitures[i / 2 % Fournitures.Length];
            var variante = i / 20 + 1;
            articles.Add(new Article(
                Code: $"{(prestation ? "PRE" : "FOU")}-{i + 1:000}",
                Designation: variante == 1 ? nom : $"{nom} (lot {variante})",
                Unite: prestation ? "h" : "u",
                PrixUnitaireHt: Math.Round((decimal)(random.NextDouble() * (prestation ? 180 : 60) + 5), 2),
                TauxTva: prestation ? 20m : Taux[random.Next(Taux.Length)]));
        }
        return articles;
    }

    public static List<LigneFacture> Lignes(int nombre)
    {
        var random = new Random(42);
        var lignes = new List<LigneFacture>(nombre);
        for (var i = 0; i < nombre; i++)
        {
            var article = Catalogue[random.Next(Catalogue.Count)];
            lignes.Add(new LigneFacture(Guid.NewGuid(), article.Code, article.Designation, random.Next(1, 25),
                article.Unite, article.PrixUnitaireHt, random.Next(4) == 0 ? 5m : 0m, article.TauxTva));
        }
        return lignes;
    }
}
