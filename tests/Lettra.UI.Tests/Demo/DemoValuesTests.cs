using Lettra.UI.Shared.Demo;

namespace Lettra.UI.Tests.Demo;

/// <summary>Valeurs propres à une lecture de la démo : code court et SIREN fictif valide, différents à chaque lancement.</summary>
public sealed class DemoValuesTests
{
    private static bool LuhnValide(string chiffres)
    {
        int somme = 0;
        for (int i = 0; i < chiffres.Length; i++)
        {
            int c = chiffres[chiffres.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                c = c * 2 > 9 ? (c * 2) - 9 : c * 2;
            }

            somme += c;
        }

        return somme % 10 == 0;
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1_761_400_000_123L)]
    [InlineData(-42L)]
    [InlineData(long.MaxValue)]
    public void Le_siren_fictif_est_valide_et_le_code_court(long graine)
    {
        DemoValues values = new(graine);

        values.Siren.Should().HaveLength(9).And.StartWith("92").And.MatchRegex("^[0-9]+$");
        LuhnValide(values.Siren).Should().BeTrue(values.Siren);
        values.Run.Should().HaveLength(4).And.MatchRegex("^[0-9A-Z]{4}$");
    }

    [Fact]
    public void La_cle_de_luhn_est_celle_d_un_siren_connu() =>
        DemoValues.WithLuhnKey("92000001").Should().Be("920000015");

    [Fact]
    public void Deux_lancements_n_ont_pas_les_memes_valeurs()
    {
        DemoValues premier = new(1_761_400_000_123L);
        DemoValues second = new(1_761_400_007_456L);

        second.Run.Should().NotBe(premier.Run);
        second.Siren.Should().NotBe(premier.Siren);
    }

    [Fact]
    public void Les_jetons_sont_remplaces_dans_les_textes()
    {
        DemoValues values = new(123_456L);

        values.Apply("ATELIER-{run} / {siren} / {autre}").Should().Be($"ATELIER-{values.Run} / {values.Siren} / {{autre}}");
        DemoValues.For(new Security.ManualClock(DateTimeOffset.UnixEpoch.AddMilliseconds(123_456))).Run.Should().Be(values.Run);
    }
}
