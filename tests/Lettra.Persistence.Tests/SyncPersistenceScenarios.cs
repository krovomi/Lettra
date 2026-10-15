using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Catalog;
using Lettra.Application.Features.Customers;
using Lettra.Application.Features.Parties;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Sync;
using Lettra.Domain.Catalog;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Persistence.Tests;

/// <summary>Synchronisation (phase 4) : journal, réception, outbox et réponses gardées, mêmes résultats sur les trois providers.</summary>
internal static class SyncPersistenceScenarios
{
    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", null, "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    private static readonly Supplier.Draft Dupuis = new(
        "Minoterie Dupuis", null, "73282932000074", null, null, null, "2 chemin du Moulin", null, "45000", "Orléans", "FR");

    private static readonly CatalogItem.Draft Conseil = new("CONSEIL", "Heure de conseil", null, CatalogItemKind.Service, "HUR", 85m, "FR-20");

    /// <summary>
    /// Chaque enregistrement d'un tiers ou d'un article ajoute une ligne au journal ; l'instantané puis le journal rendent
    /// les éléments dans leur état actuel, filtrés selon les droits ; l'outbox et les réponses gardées font l'aller-retour.
    /// </summary>
    public static async Task JournalFeedOutboxAndReceiptsAsync(TenantHarness harness, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        CustomerId customer = CustomerId.New();
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ICommandDispatcher commands = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            (await commands.SendAsync(new CreateCustomerCommand(dossier, Martin, customer), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new CreateSupplierCommand(dossier, Dupuis), default)).IsSuccess.Should().BeTrue();
            (await commands.SendAsync(new CreateCatalogItemCommand(dossier, Conseil), default)).IsSuccess.Should().BeTrue();
        }

        SyncChangesResponse snapshot;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            ISyncChangeFeed feed = scope.ServiceProvider.GetRequiredService<ISyncChangeFeed>();
            snapshot = (await feed.ReadAsync(null, 2, new SyncFeedScope(true, true), default)).Value;
            snapshot.HasMore.Should().BeTrue();
            snapshot.Count.Should().Be(2);
            SyncChangesResponse rest = (await feed.ReadAsync(snapshot.Token, 2, new SyncFeedScope(true, true), default)).Value;
            rest.CatalogItems.Should().ContainSingle();
            rest.HasMore.Should().BeFalse();
            rest.Token.Should().Be("j:3", "trois enregistrements, trois lignes continues");
            snapshot = rest;

            (await feed.ReadAsync(null, 10, new SyncFeedScope(true, false), default)).Value.CatalogItems.Should().BeEmpty();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IQueryDispatcher queries = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
            CustomerResponse read = (await queries.QueryAsync(new GetCustomerQuery(dossier, customer), default)).Value;
            Result updated = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(
                new UpdateCustomerCommand(dossier, customer, Martin with { City = "Lyon", PostalCode = "69002" }, read.Version), default);
            updated.IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            SyncChangesResponse changes = (await scope.ServiceProvider.GetRequiredService<ISyncChangeFeed>()
                .ReadAsync(snapshot.Token, 10, new SyncFeedScope(true, true), default)).Value;
            changes.Token.Should().Be("j:4");
            changes.Customers.Should().ContainSingle().Which.Should().Match<CustomerResponse>(c => c.City == "Lyon" && c.Version == 2);
            changes.Suppliers.Should().BeEmpty();

            // Outbox : écrite par l'enregistrement de la commande, acquittée avec un conflit gardé.
            SyncOutboxStaging staging = scope.ServiceProvider.GetRequiredService<SyncOutboxStaging>();
            CreateCustomerIntent intent = new(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, new CreateCustomerRequest { Name = "Durand" });
            staging.Stage(intent);
            await scope.ServiceProvider.GetRequiredService<Application.Abstractions.Persistence.IUnitOfWork>().SaveChangesAsync(default);
            ISyncOutbox outbox = scope.ServiceProvider.GetRequiredService<ISyncOutbox>();
            (await outbox.ListAsync(SyncOutboxStatus.Pending, default)).Should().ContainSingle().Which.Intent.Should().BeEquivalentTo(intent);
            System.Text.Json.JsonElement value = System.Text.Json.JsonDocument.Parse("\"x\"").RootElement;
            await outbox.AcknowledgeAsync(
                [new SyncIntentResult(intent.IdempotencyKey, intent.EntityId, SyncIntentOutcome.Conflict, 3, [new SyncFieldConflict("Name", value, value, value)], [])],
                DateTimeOffset.UtcNow, default);
            (await outbox.ListAsync(null, default)).Single().Status.Should().Be(SyncOutboxStatus.Conflict);
            (await outbox.ListConflictsAsync(default)).Should().ContainSingle().Which.Conflict.Field.Should().Be("Name");

            // Réponses gardées par clé.
            ISyncReceipts receipts = scope.ServiceProvider.GetRequiredService<ISyncReceipts>();
            SyncReceipt receipt = new(intent.IdempotencyKey, Guid.CreateVersion7(), new string('a', 64),
                new SyncIntentResult(intent.IdempotencyKey, intent.EntityId, SyncIntentOutcome.Applied, 1, [], [new ErrorDetail("X.Y", "Name")]), DateTimeOffset.UtcNow);
            await receipts.SaveAsync(receipt, default);
            await receipts.SaveAsync(receipt, default);
            (await receipts.FindAsync(intent.IdempotencyKey, default)).Should().BeEquivalentTo(receipt, o => o.Excluding(r => r.ReceivedAt));
        }
    }
}
