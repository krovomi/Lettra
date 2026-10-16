using System.Text.RegularExpressions;

namespace Lettra.UI.Tests.Design;

/// <summary>
/// Cellules de la grille de saisie : un champ de cellule a la hauteur de sa ligne. Le style commun des champs
/// (lettra.css) pose une hauteur minimale de 2,25 rem ; si la grille ne la neutralise pas, le champ déborde de la ligne
/// de 24 px et le texte est rogné en bas.
/// </summary>
public sealed partial class GridCellTests
{
    private static readonly string Css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "lettra-grid.css"));
    private static readonly string AppCss = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "lettra.css"));

    [Fact]
    public void Un_champ_de_cellule_n_a_pas_de_hauteur_minimale_propre()
    {
        Match regle = CellRule().Match(Css);

        regle.Success.Should().BeTrue();
        regle.Groups["body"].Value.Should().MatchRegex(@"min-height:\s*0;").And.MatchRegex(@"height:\s*100%;");
    }

    [Fact]
    public void Une_ligne_ne_depasse_la_grille_que_sous_la_largeur_minimale_de_ses_colonnes()
    {
        // max-content faisait défiler la grille dès que la plus longue désignation ne tenait pas : « Total HT » était rogné.
        Match regle = RowRule().Match(Css);

        regle.Success.Should().BeTrue();
        regle.Groups["body"].Value.Should().MatchRegex(@"min-width:\s*min-content;").And.NotMatchRegex(@"min-width:\s*max-content;");
    }

    [Fact]
    public void Un_champ_de_formulaire_tient_dans_sa_colonne()
    {
        // Sans min-width, une liste prend la largeur de sa plus longue option et sort de la carte (« Régime de TVA »).
        AppCss.Should().MatchRegex(@"\.lettra-field\s*\{[^}]*min-width:\s*0;")
            .And.MatchRegex(@"\.lettra-field > :where\(input[^{]*select[^{]*\{[^}]*width:\s*100%;\s*min-width:\s*0;");
    }

    [GeneratedRegex(@"\.lg-input,\s*\.lg-calc\s*\{(?<body>[^}]*)\}")]
    private static partial Regex CellRule();

    [GeneratedRegex(@"\n\.lg-row\s*\{(?<body>[^}]*)\}")]
    private static partial Regex RowRule();
}
