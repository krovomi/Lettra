using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Tri et filtre de nature des listes de clients, de fournisseurs et d'articles, sur les trois providers.</summary>
internal static class ReferenceListScenarios
{
    // Une entreprise a besoin de son SIREN et de son SIRET (numéros réels valides), un particulier non.
    private static Customer.Draft Customer(CustomerKind kind, string name, string city, string? siren, string? siret) =>
        new(kind, name, siren, siret, null, null, "1 rue de la Paix", null, "75002", city, "FR");

    private static Supplier.Draft Supplier(string name, string city) => new(
        name, null, null, null, null, null, "Rue Neuve 1", null, "1000", city, "BE");

    public static async Task SortAndKindAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        foreach ((CustomerKind kind, string name, string city, string? siren, string? siret) in new (CustomerKind, string, string, string?, string?)[]
        {
            (CustomerKind.Company, "Boulangerie Martin", "Paris", "732829320", "73282932000074"),
            (CustomerKind.Individual, "Zoé Lambert", "Bordeaux", null, null),
            (CustomerKind.Company, "Atelier Dupont", "Nantes", "552100554", "55210055400013"),
            (CustomerKind.Company, "Garage du Centre", "Nantes", "443061841", "44306184100047"),
        })
        {
            (await commands.SendAsync(new CreateCustomerCommand(dossier, Customer(kind, name, city, siren, siret)), default)).IsSuccess.Should().BeTrue();
        }

        (await commands.SendAsync(new CreateSupplierCommand(dossier, Supplier("Papeterie Dupuis", "Liège")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateSupplierCommand(dossier, Supplier("Électricité Générale", "Anvers")), default)).IsSuccess.Should().BeTrue();

        IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        async Task<string[]> CustomersAsync(ListCustomersQuery query) => [.. (await queries.QueryAsync(query, default)).Value.Items.Select(c => c.Name)];
        async Task<string[]> SuppliersAsync(ListSuppliersQuery query) => [.. (await queries.QueryAsync(query, default)).Value.Items.Select(s => s.Name)];

        (await CustomersAsync(new ListCustomersQuery(dossier)))
            .Should().Equal("Atelier Dupont", "Boulangerie Martin", "Garage du Centre", "Zoé Lambert");
        (await CustomersAsync(new ListCustomersQuery(dossier, Direction: SortDirectionDto.Descending)))
            .Should().Equal("Zoé Lambert", "Garage du Centre", "Boulangerie Martin", "Atelier Dupont");

        // Même ville : le nom départage, de A à Z quel que soit le sens de la ville.
        (await CustomersAsync(new ListCustomersQuery(dossier, Sort: PartySortDto.City)))
            .Should().Equal("Zoé Lambert", "Atelier Dupont", "Garage du Centre", "Boulangerie Martin");
        (await CustomersAsync(new ListCustomersQuery(dossier, Sort: PartySortDto.City, Direction: SortDirectionDto.Descending)))
            .Should().Equal("Boulangerie Martin", "Atelier Dupont", "Garage du Centre", "Zoé Lambert");

        // La nature filtre et le total suit ; la page suivante continue dans le même ordre.
        PagedResponse<CustomerSummary> companies = (await queries.QueryAsync(
            new ListCustomersQuery(dossier, 1, 2, null, PartySortDto.City, SortDirectionDto.Ascending, CustomerKindDto.Company), default)).Value;
        companies.TotalCount.Should().Be(3);
        companies.Items.Select(c => c.Name).Should().Equal("Atelier Dupont", "Garage du Centre");
        (await CustomersAsync(new ListCustomersQuery(dossier, 2, 2, null, PartySortDto.City, SortDirectionDto.Ascending, CustomerKindDto.Company)))
            .Should().Equal("Boulangerie Martin");
        (await CustomersAsync(new ListCustomersQuery(dossier, Kind: CustomerKindDto.Individual))).Should().Equal("Zoé Lambert");

        (await SuppliersAsync(new ListSuppliersQuery(dossier, Sort: PartySortDto.City))).Should().Equal("Électricité Générale", "Papeterie Dupuis");
        (await SuppliersAsync(new ListSuppliersQuery(dossier, Sort: PartySortDto.City, Direction: SortDirectionDto.Descending)))
            .Should().Equal("Papeterie Dupuis", "Électricité Générale");
    }

    private static CatalogItem.Draft Item(string reference, string designation, CatalogItemKind kind, decimal price) =>
        new(reference, designation, null, kind, kind == CatalogItemKind.Service ? "HUR" : "C62", price, "FR-20");

    public static async Task CatalogSortAndKindAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        foreach (CatalogItem.Draft item in new[]
        {
            Item("PRES-1", "Tenue comptable", CatalogItemKind.Service, 150m),
            Item("CONS-1", "Écriture de bilan", CatalogItemKind.Service, 1200.5m),
            Item("FOUR-1", "Classeur", CatalogItemKind.Good, 4.9m),
        })
        {
            (await commands.SendAsync(new CreateCatalogItemCommand(dossier, item), default)).IsSuccess.Should().BeTrue();
        }

        IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        async Task<string[]> ReferencesAsync(ListCatalogItemsQuery query) => [.. (await queries.QueryAsync(query, default)).Value.Items.Select(i => i.Reference)];

        (await ReferencesAsync(new ListCatalogItemsQuery(dossier))).Should().Equal("CONS-1", "FOUR-1", "PRES-1");
        (await ReferencesAsync(new ListCatalogItemsQuery(dossier, Direction: SortDirectionDto.Descending))).Should().Equal("PRES-1", "FOUR-1", "CONS-1");

        // « Écriture » se range à E, sans accent, sur les trois bases.
        (await ReferencesAsync(new ListCatalogItemsQuery(dossier, Sort: CatalogSortDto.Designation))).Should().Equal("FOUR-1", "CONS-1", "PRES-1");
        (await ReferencesAsync(new ListCatalogItemsQuery(dossier, Sort: CatalogSortDto.UnitPrice, Direction: SortDirectionDto.Descending)))
            .Should().Equal("CONS-1", "PRES-1", "FOUR-1");
        (await ReferencesAsync(new ListCatalogItemsQuery(dossier, Sort: CatalogSortDto.UnitPrice, Kind: CatalogItemKindDto.Service)))
            .Should().Equal("PRES-1", "CONS-1");
        (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Kind: CatalogItemKindDto.Good), default)).Value.TotalCount.Should().Be(1);
    }
}
