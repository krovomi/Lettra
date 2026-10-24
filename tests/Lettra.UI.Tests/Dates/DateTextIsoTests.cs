using Lettra.UI.Shared.Components.Dates;

namespace Lettra.UI.Tests.Dates;

/// <summary>Dates ISO des adresses et des attributs : indépendantes de la langue de l'écran.</summary>
public sealed class DateTextIsoTests
{
    [Fact]
    public void Une_date_s_ecrit_et_se_relit_en_ISO()
    {
        DateText.ToIso(new DateOnly(2026, 3, 9)).Should().Be("2026-03-09");
        DateText.ParseIso("2026-03-09").Should().Be(new DateOnly(2026, 3, 9));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("09/03/2026")]
    [InlineData("2026-02-30")]
    public void Ce_qui_n_est_pas_une_date_ISO_ne_se_lit_pas(string? text) =>
        DateText.ParseIso(text).Should().BeNull();

    [Theory]
    [InlineData("2026-10", 2026, 10)]
    [InlineData("2026-01", 2026, 1)]
    public void Un_mois_ISO_donne_son_premier_jour(string text, int year, int month) =>
        DateText.ParseIsoMonth(text).Should().Be(new DateOnly(year, month, 1));

    [Theory]
    [InlineData(null)]
    [InlineData("2026-13")]
    [InlineData("2026-T3")]
    public void Un_mois_illisible_ne_donne_rien(string? text) =>
        DateText.ParseIsoMonth(text).Should().BeNull();
}
