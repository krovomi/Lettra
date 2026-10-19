using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Catalog;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Domain.Catalog;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Tri et nature de la liste des articles : ce que la validation refuse, ce qui part au stockage.</summary>
public sealed class CatalogListTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    [Fact]
    public void Colonne_sens_ou_nature_inconnus_refuses()
    {
        new ListCatalogItemsValidator().Validate(new ListCatalogItemsQuery(Dossier, Sort: CatalogSortDto.UnitPrice, Kind: CatalogItemKindDto.Good))
            .Should().BeEmpty();
        new ListCatalogItemsValidator().Validate(new ListCatalogItemsQuery(Dossier, Sort: 0, Direction: (SortDirectionDto)5, Kind: (CatalogItemKindDto)9))
            .Select(e => e.Target).Should().Equal("Kind", "Sort", "Direction");
    }

    [Fact]
    public async Task Les_criteres_arrivent_au_stockage_avec_la_nature_du_domaine()
    {
        InMemoryPartiesAndCatalog store = new();
        await new ListCatalogItemsHandler(store).HandleAsync(
            new ListCatalogItemsQuery(Dossier, " cons ", true, 2, 20, CatalogSortDto.Designation, SortDirectionDto.Descending, CatalogItemKindDto.Service), default);

        store.LastFilter.Should().Be(new CatalogItemFilter("cons", true, 2, 20, CatalogSortDto.Designation, SortDirectionDto.Descending, CatalogItemKind.Service));
    }
}
