using Lettra.Contracts.Common;

namespace Lettra.Application.Tests;

/// <summary>Le texte recherché d'une liste, normalisé de la même façon par l'écran, l'API et les cas d'usage.</summary>
public sealed class ListSearchTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" Martin ", "Martin")]
    [InlineData("FAC-2026", "FAC-2026")]
    public void Le_texte_est_sans_espaces_autour_et_absent_quand_il_est_vide(string? search, string? expected) =>
        ListSearch.Normalize(search).Should().Be(expected);
}
