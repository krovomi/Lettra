using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Domain.Catalog;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Search;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Recherche sans accents ni casse sur les Tiers et le Catalogue : mêmes scénarios sur les trois providers.</summary>
internal static class AccentInsensitiveSearchScenarios
{
    private static Customer.Draft Customer(string name) => new(
        CustomerKind.Individual, name, null, null, null, null, "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static Supplier.Draft Supplier(string name) => new(
        name, null, null, null, null, null, "Rue Neuve 1", null, "1000", "Bruxelles", "BE");

    private static CatalogItem.Draft Item(string reference, string designation) =>
        new(reference, designation, null, CatalogItemKind.Service, "HUR", 80m, "FR-20");

    public static async Task SearchIgnoresAccentsAndCaseAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await SeedAsync(harness, dossier);

        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        await ExpectMatchesAsync(scope.ServiceProvider.GetRequiredService<IQueryDispatcher>(), dossier);
    }

    /// <summary>
    /// Lignes écrites avant la colonne : la migration les remplit en SQL. On efface la colonne, on rejoue le SQL
    /// de la migration, et la recherche doit trouver la même chose qu'avec le calcul C#.
    /// </summary>
    public static async Task BackfillMatchesTheCSharpFormAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await SeedAsync(harness, dossier);

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            LettraDbContext db = await scope.ServiceProvider.GetRequiredService<TenantDbSession>().GetAsync(default);
            bool sqlServer = provider == DatabaseProvider.SqlServer;
            foreach ((string table, string column) in new[] { ("Customers", "Name"), ("Suppliers", "Name"), ("CatalogItems", "Designation") })
            {
                string quoted = sqlServer ? $"[{table}]" : $"\"{table}\"";
                string key = sqlServer ? "[SearchKey]" : "\"SearchKey\"";
#pragma warning disable EF1002 // Identifiants fixes du test, aucune donnée externe.
                await db.Database.ExecuteSqlRawAsync($"UPDATE {quoted} SET {key} = ''");
#pragma warning restore EF1002
                await db.Database.ExecuteSqlRawAsync(SearchKeyBackfill.Sql(table, column, sqlServer));
            }

            List<string> keys = await db.Set<Customer>().Select(c => EF.Property<string>(c, SearchKey.PropertyName)).ToListAsync();
            keys.Should().Contain(["BOULANGERIE ECLAIR", "SOCIETE COEUR D'OEUVRE"]);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            await ExpectMatchesAsync(scope.ServiceProvider.GetRequiredService<IQueryDispatcher>(), dossier);
        }
    }

    private static async Task SeedAsync(TenantHarness harness, DossierId dossier)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        (await commands.SendAsync(new CreateCustomerCommand(dossier, Customer("Boulangerie Éclair")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateCustomerCommand(dossier, Customer("Société Cœur d’Œuvre")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateCustomerCommand(dossier, Customer("Garage Martin")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateSupplierCommand(dossier, Supplier("Électricité Générale")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateSupplierCommand(dossier, Supplier("Papeterie Dupuis")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateCatalogItemCommand(dossier, Item("PRES-1", "Prestation de comptabilité")), default)).IsSuccess.Should().BeTrue();
        (await commands.SendAsync(new CreateCatalogItemCommand(dossier, Item("CONS-1", "Conseil fiscal")), default)).IsSuccess.Should().BeTrue();
    }

    private static async Task ExpectMatchesAsync(IQueryDispatcher queries, DossierId dossier)
    {
        (await queries.QueryAsync(new ListCustomersQuery(dossier, Search: "eclair"), default)).Value.Items
            .Select(c => c.Name).Should().Equal("Boulangerie Éclair");
        (await queries.QueryAsync(new ListCustomersQuery(dossier, Search: "ÉCLAIR"), default)).Value.Items
            .Select(c => c.Name).Should().Equal("Boulangerie Éclair");
        (await queries.QueryAsync(new ListCustomersQuery(dossier, Search: "coeur d'oeuvre"), default)).Value.Items
            .Select(c => c.Name).Should().Equal("Société Cœur d’Œuvre");
        (await queries.QueryAsync(new ListCustomersQuery(dossier, Search: "societe"), default)).Value.TotalCount.Should().Be(1);
        (await queries.QueryAsync(new ListCustomersQuery(dossier), default)).Value.TotalCount.Should().Be(3);

        (await queries.QueryAsync(new ListSuppliersQuery(dossier, Search: "electricite gen"), default)).Value.Items
            .Select(s => s.Name).Should().Equal("Électricité Générale");

        (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Search: "comptabilite"), default)).Value.Items
            .Select(i => i.Reference).Should().Equal("PRES-1");
        (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Search: "cons"), default)).Value.Items
            .Select(i => i.Reference).Should().Equal("CONS-1");
    }
}
