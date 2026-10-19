using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Tests;

/// <summary>Tri et nature des listes de clients et de fournisseurs : ce que la validation refuse, ce qui part au stockage.</summary>
public sealed class PartyListTests
{
    private static readonly DossierId Dossier = new(Guid.CreateVersion7());

    [Fact]
    public void Tri_par_ville_et_nature_acceptes()
    {
        new ListCustomersValidator().Validate(new ListCustomersQuery(
            Dossier, Sort: PartySortDto.City, Direction: SortDirectionDto.Descending, Kind: CustomerKindDto.Individual)).Should().BeEmpty();
        new ListSuppliersValidator().Validate(new ListSuppliersQuery(Dossier, Sort: PartySortDto.City)).Should().BeEmpty();
    }

    [Fact]
    public void Colonne_sens_ou_nature_inconnus_refuses()
    {
        new ListCustomersValidator().Validate(new ListCustomersQuery(Dossier, Sort: (PartySortDto)9, Direction: 0, Kind: (CustomerKindDto)7))
            .Select(e => e.Target).Should().Equal("Kind", "Sort", "Direction");
        new ListSuppliersValidator().Validate(new ListSuppliersQuery(Dossier, Direction: (SortDirectionDto)3))
            .Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Validation.Invalid);
    }

    [Fact]
    public async Task Les_criteres_arrivent_au_stockage_avec_la_nature_du_domaine()
    {
        InMemoryCustomers customers = new();
        await new ListCustomersHandler(customers).HandleAsync(
            new ListCustomersQuery(Dossier, 2, 10, "dup", PartySortDto.City, SortDirectionDto.Descending, CustomerKindDto.Company), default);

        customers.LastFilter.Should().Be(new PartyListFilter("dup", 2, 10, PartySortDto.City, SortDirectionDto.Descending, CustomerKind.Company));

        InMemoryPartiesAndCatalog suppliers = new();
        await new ListSuppliersHandler(suppliers).HandleAsync(new ListSuppliersQuery(Dossier, Sort: PartySortDto.City), default);

        suppliers.LastPartyFilter.Should().Be(new PartyListFilter(null, 1, 50, PartySortDto.City, SortDirectionDto.Ascending));
    }
}
