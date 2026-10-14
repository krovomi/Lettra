using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Suppliers;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Fournisseurs et catalogue : mêmes scénarios sur SQLite, PostgreSQL et SQL Server.</summary>
internal static class PartiesAndCatalogPersistenceScenarios
{
    private static readonly Supplier.Draft Dupuis = new(
        "Minoterie Dupuis", null, "73282932000074", "FR44732829320", "factures@dupuis.fr", "FR7630006000011234567890189",
        "2 chemin du Moulin", "Zone artisanale", "45000", "Orléans", "FR");

    private static CatalogItem.Draft Item(string reference, string designation, decimal price = 85.1234m, string vat = "FR-5.5") =>
        new(reference, designation, "Accompagnement « clé en main »", CatalogItemKind.Service, "HUR", price, vat);

    public static async Task SupplierRoundTripAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        SupplierId id;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            id = (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new CreateSupplierCommand(dossier, Dupuis), default)).Value;
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            SupplierResponse read = (await queries.QueryAsync(new GetSupplierQuery(dossier, id), default)).Value;
            read.Should().BeEquivalentTo(new
            {
                Name = "Minoterie Dupuis",
                Siren = "732829320",
                Siret = "73282932000074",
                VatNumber = "FR44732829320",
                Iban = "FR7630006000011234567890189",
                AddressLine2 = "Zone artisanale",
                City = "Orléans",
                CountryCode = "FR",
            });

            (await queries.QueryAsync(new ListSuppliersQuery(dossier), default)).Value.Items
                .Should().ContainSingle().Which.Should().Be(new SupplierSummary(id.Value, "Minoterie Dupuis", "732829320", "Orléans"));
        }
    }

    /// <summary>Prix à 4 décimales, unité et taux relus à l'identique ; modification et archivage persistés.</summary>
    public static async Task CatalogRoundTripAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        CatalogItemId id;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            id = (await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                .SendAsync(new CreateCatalogItemCommand(dossier, Item("Prés-01", "Heure de conseil")), default)).Value;
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new UpdateCatalogItemCommand(dossier, id, Item("Prés-01", "Heure de conseil", price: 90.5m, vat: "FR-20")), default))
                .IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new SetCatalogItemArchivedCommand(dossier, id, true), default)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            CatalogItemResponse read = (await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                .QueryAsync(new GetCatalogItemQuery(dossier, id), default)).Value;
            read.Should().BeEquivalentTo(new
            {
                Reference = "Prés-01",
                Designation = "Heure de conseil",
                Description = "Accompagnement « clé en main »",
                Kind = CatalogItemKindDto.Service,
                UnitCode = "HUR",
                UnitPriceExclTax = 90.5m,
                VatRateCode = "FR-20",
                VatRatePercent = 20m,
                IsArchived = true,
            });
        }
    }

    /// <summary>Recherche sans tenir compte de la casse, archivés exclus par défaut, unicité garantie par la base.</summary>
    public static async Task CatalogSearchAndUniquenessAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            await commands.SendAsync(new CreateCatalogItemCommand(dossier, Item("CONS-1", "Conseil fiscal")), default);
            await commands.SendAsync(new CreateCatalogItemCommand(dossier, Item("SAISIE", "Tenue de comptabilité")), default);
            CatalogItemId archived = (await commands.SendAsync(new CreateCatalogItemCommand(dossier, Item("VIEUX", "Conseil archivé")), default)).Value;
            await commands.SendAsync(new SetCatalogItemArchivedCommand(dossier, archived, true), default);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            PagedResponse<CatalogItemSummary> found = (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Search: "conseil"), default)).Value;
            found.Items.Select(i => i.Reference).Should().Equal("CONS-1");

            PagedResponse<CatalogItemSummary> byReference = (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Search: "sai"), default)).Value;
            byReference.Items.Select(i => i.Reference).Should().Equal("SAISIE");

            PagedResponse<CatalogItemSummary> all = (await queries.QueryAsync(new ListCatalogItemsQuery(dossier, Search: "CONSEIL", IncludeArchived: true), default)).Value;
            all.Items.Select(i => i.Reference).Should().Equal("CONS-1", "VIEUX");
        }

        // Deux écritures concurrentes passent le contrôle applicatif : l'index unique de la base tranche.
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            CatalogItem duplicate = CatalogItem.Create(CatalogItemId.New(), Item("cons-1", "Doublon"), DateTimeOffset.UtcNow).Value;
            await scope.ServiceProvider.GetRequiredService<ICatalogItemRepository>().AddAsync(duplicate, default);

            Func<Task> save = () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
            await save.Should().ThrowAsync<DbUpdateException>();
        }
    }
}
