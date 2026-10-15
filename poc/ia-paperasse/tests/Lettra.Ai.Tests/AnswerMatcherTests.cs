using Lettra.Ai.Evals;

namespace Lettra.Ai.Tests;

public sealed class AnswerMatcherTests
{
    [Theory]
    [InlineData("Date d'échéance manquante", "echeance")]
    [InlineData("La CATÉGORIE D’OPÉRATION manque", "Catégorie d'opération")]
    [InlineData("Total : 2 400,00 €", "2400,00 EUR")]
    [InlineData("Total : 2 400,00 EUR", "2400,00 EUR")]
    [InlineData("Total : 2400.00 EUR", "2400,00 EUR")]
    [InlineData("Total : 2 400 euros", "2400,00 EUR")]
    [InlineData("soit 1.234,50 €", "1234,5 EUR")]
    [InlineData("à confirmer par l'expert-comptable du dossier", "À confirmer par l'expert-comptable")]
    public void Accepte_les_variantes_d_ecriture(string answer, string expected) =>
        Assert.True(AnswerMatcher.Contains(answer, expected));

    [Theory]
    [InlineData("Total : 2 400,01 €", "2400,00 EUR")]
    [InlineData("Total : 24 000,00 €", "2400,00 EUR")]
    [InlineData("Facture conformes", "conforme")]
    [InlineData("136,99 EUR", "36,99 EUR")]
    public void Ne_confond_pas_des_montants_ou_textes_differents(string answer, string expected) =>
        Assert.False(AnswerMatcher.Contains(answer, expected));
}
