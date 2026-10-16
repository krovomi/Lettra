namespace Lettra.Speech.Tests;

public sealed class SpokenNumbersTests
{
    private static decimal? Read(string text, SpokenLanguage language = SpokenLanguage.French)
    {
        string source = SpokenText.Prepare(text);
        List<SpokenToken> tokens = SpokenText.Tokenize(source);
        return SpokenNumbers.TryRead(tokens, 0, language, out decimal value, out int next) && next == tokens.Count ? value : null;
    }

    [Theory]
    [InlineData("trois", 3)]
    [InlineData("un", 1)]
    [InlineData("une", 1)]
    [InlineData("dix-sept", 17)]
    [InlineData("vingt et un", 21)]
    [InlineData("vingt-et-un", 21)]
    [InlineData("trente-deux", 32)]
    [InlineData("soixante-dix", 70)]
    [InlineData("soixante et onze", 71)]
    [InlineData("soixante-dix-sept", 77)]
    [InlineData("quatre-vingts", 80)]
    [InlineData("quatre-vingt-dix-neuf", 99)]
    [InlineData("quatre vingt onze", 91)]
    [InlineData("cent", 100)]
    [InlineData("cent vingt", 120)]
    [InlineData("deux cents", 200)]
    [InlineData("deux cent cinquante", 250)]
    [InlineData("mille", 1000)]
    [InlineData("mille deux cents", 1200)]
    [InlineData("deux mille trois cent quarante-cinq", 2345)]
    [InlineData("un million", 1000000)]
    [InlineData("nonante-cinq", 95)]
    [InlineData("septante", 70)]
    [InlineData("120", 120)]
    [InlineData("120,50", 120.50)]
    [InlineData("1.5", 1.5)]
    [InlineData("1 200", 1200)]
    [InlineData("12 345 678", 12345678)]
    [InlineData("deux virgule cinq", 2.5)]
    [InlineData("trois virgule soixante-quinze", 3.75)]
    [InlineData("deux virgule zéro cinq", 2.05)]
    [InlineData("deux et demi", 2.5)]
    [InlineData("demi", 0.5)]
    [InlineData("une demi", 0.5)]
    [InlineData("2 mille", 2000)]
    public void Les_nombres_francais_sont_lus(string text, double expected) =>
        Read(text).Should().Be((decimal)expected);

    [Theory]
    [InlineData("three", 3)]
    [InlineData("twenty-one", 21)]
    [InlineData("one hundred and five", 105)]
    [InlineData("two thousand three hundred", 2300)]
    [InlineData("two point five", 2.5)]
    [InlineData("two and a half", 2.5)]
    [InlineData("half", 0.5)]
    public void Les_nombres_anglais_sont_lus(string text, double expected) =>
        Read(text, SpokenLanguage.English).Should().Be((decimal)expected);

    [Theory]
    [InlineData("deux trois")]
    [InlineData("vingt cent")]
    [InlineData("cinq six")]
    public void Deux_nombres_qui_se_suivent_ne_se_fondent_pas(string text) =>
        Read(text).Should().BeNull();

    [Fact]
    public void La_lecture_s_arrete_au_premier_mot_qui_n_est_pas_un_nombre()
    {
        List<SpokenToken> tokens = SpokenText.Tokenize("trois heures de conseil");
        SpokenNumbers.TryRead(tokens, 0, SpokenLanguage.French, out decimal value, out int next).Should().BeTrue();
        value.Should().Be(3);
        next.Should().Be(1);
    }

    [Theory]
    [InlineData("conseil")]
    [InlineData("et")]
    [InlineData("")]
    public void Un_mot_qui_n_est_pas_un_nombre_n_est_pas_lu(string text) =>
        Read(text).Should().BeNull();
}
