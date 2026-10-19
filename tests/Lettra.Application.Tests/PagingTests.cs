using Lettra.Application.Features;

namespace Lettra.Application.Tests;

/// <summary>La règle commune des listes paginées, partagée par les requêtes de liste du cœur et du module Exports.</summary>
public sealed class PagingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, Paging.MaxPageSize)]
    public void Une_page_et_une_taille_dans_les_bornes_passent(int page, int pageSize) =>
        Paging.Validate(page, pageSize).Should().BeEmpty();

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, Paging.MaxPageSize + 1, "PageSize")]
    public void Une_page_ou_une_taille_hors_bornes_est_refusee(int page, int pageSize, string target) =>
        Paging.Validate(page, pageSize).Should().ContainSingle().Which.Target.Should().Be(target);

    [Fact]
    public void Une_liste_peut_declarer_une_taille_maximale_plus_grande() =>
        Paging.Validate(1, 500, maxPageSize: 500).Should().BeEmpty();

    [Fact]
    public void Les_deux_erreurs_se_cumulent() =>
        Paging.Validate(0, 0).Select(e => e.Target).Should().Equal("Page", "PageSize");
}
