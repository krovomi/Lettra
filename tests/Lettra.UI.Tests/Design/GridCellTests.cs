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

    [Fact]
    public void Un_champ_de_cellule_n_a_pas_de_hauteur_minimale_propre()
    {
        Match regle = CellRule().Match(Css);

        regle.Success.Should().BeTrue();
        regle.Groups["body"].Value.Should().MatchRegex(@"min-height:\s*0;").And.MatchRegex(@"height:\s*100%;");
    }

    [GeneratedRegex(@"\.lg-input,\s*\.lg-calc\s*\{(?<body>[^}]*)\}")]
    private static partial Regex CellRule();
}
