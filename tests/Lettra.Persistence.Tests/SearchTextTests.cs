using Lettra.Infrastructure.Persistence.Search;

namespace Lettra.Persistence.Tests;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Boulangerie Éclair", "BOULANGERIE ECLAIR")]
    [InlineData("Société Cœur d’Œuvre", "SOCIETE COEUR D'OEUVRE")]
    [InlineData("  Ça   déménage\t! ", "CA DEMENAGE !")]
    [InlineData("Ex æquo, Straße", "EX AEQUO, STRASSE")]
    [InlineData("Noël à l'Haÿ-les-Roses", "NOEL A L'HAY-LES-ROSES")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void La_forme_de_recherche_retire_accents_casse_ligatures_et_espaces_superflus(string? text, string expected) =>
        SearchText.Normalize(text).Should().Be(expected);

    [Fact]
    public void Chaque_caractere_traite_par_le_remplissage_SQL_a_la_meme_forme_qu_en_CSharp()
    {
        foreach ((string character, _) in SearchKeyBackfill.HandledReplacements)
        {
            string sqlForm = Simulate(character);
            sqlForm.Should().Be(SearchText.Normalize(character), $"« {character} » doit donner la même forme en SQL et en C#");
        }
    }

    [Fact]
    public void Le_SQL_de_remplissage_cite_les_identifiants_selon_le_provider()
    {
        SearchKeyBackfill.Sql("Customers", "Name").Should().StartWith("UPDATE \"Customers\" SET \"SearchKey\" = UPPER(").And.Contain("'é', 'e'").And.Contain("'’', '''')");
        SearchKeyBackfill.Sql("Customers", "Name", sqlServer: true).Should().StartWith("UPDATE [Customers] SET [SearchKey] = UPPER(").And.Contain("N'é', 'e'");
    }

    /// <summary>Le SQL applique les remplacements dans l'ordre puis UPPER (ASCII seulement à ce stade).</summary>
    private static string Simulate(string character)
    {
        string value = character;
        foreach ((string from, string to) in SearchKeyBackfill.HandledReplacements)
        {
            value = value.Replace(from, to, StringComparison.Ordinal);
        }

        return value.ToUpperInvariant();
    }
}
