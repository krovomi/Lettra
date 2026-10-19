using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Outbox;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence.Outbox;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lettra.Persistence.Tests;

/// <summary>
/// Destinataire simulé : il reconnaît la clé d'idempotence, comme la plateforme agréée, et peut échouer sur commande.
/// Partagé par toutes les portées du scénario.
/// </summary>
internal sealed class FakeRecipient
{
    public ConcurrentDictionary<string, int> Received { get; } = new();

    public List<(string Key, int Attempt)> Calls { get; } = [];

    /// <summary>Échecs passagers à renvoyer avant d'accepter.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>Appelé après réception, avant la réponse : simule un arrêt du processus en plein envoi.</summary>
    public Action? AfterReceive { get; set; }

    public OutboxOutcome Receive(OutboxDelivery delivery)
    {
        lock (Calls)
        {
            Calls.Add((delivery.IdempotencyKey, delivery.Attempt));
        }

        if (delivery.Payload == "poison")
        {
            return OutboxOutcome.Poison("Contenu refusé.");
        }

        if (delivery.Payload == "throw")
        {
            throw new InvalidOperationException("panne");
        }

        if (FailuresBeforeSuccess > 0)
        {
            FailuresBeforeSuccess--;
            return OutboxOutcome.Retry("Destinataire injoignable.");
        }

        Received.AddOrUpdate(delivery.IdempotencyKey, 1, (_, n) => n + 1);
        AfterReceive?.Invoke();
        return OutboxOutcome.Delivered;
    }
}

internal sealed class FakeRecipientHandler(FakeRecipient recipient) : IOutboxHandler
{
    public const string Name = "test.envoi";

    public string Kind => Name;

    public Task<OutboxOutcome> HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken) =>
        Task.FromResult(recipient.Receive(delivery));
}

/// <summary>Outbox du serveur (étape 5.4) : mêmes résultats sur les trois providers.</summary>
internal static class OutboxScenarios
{
    public static readonly DateTimeOffset Start = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public static TenantHarness Harness(IDbProviderConfigurator configurator, ManualClock clock, FakeRecipient recipient, bool inline = true) =>
        new(configurator, clock, configure: services =>
        {
            services.AddSingleton(recipient);
            services.AddScoped<IOutboxHandler, FakeRecipientHandler>();
            services.Configure<OutboxOptions>(o =>
            {
                o.MaxAttempts = 3;
                o.DispatchInline = inline;
            });
        });

    private static readonly Customer.Draft Martin = new(
        CustomerKind.Company, "Boulangerie Martin", null, "73282932000074", null, "compta@martin.fr",
        "1 rue de la Paix", null, "75002", "Paris", "FR");

    /// <summary>
    /// Le message est écrit par le SaveChanges du cas d'usage, avec le changement métier, ou pas du tout ; une même clé
    /// ne donne qu'un message ; le chemin synchrone l'envoie aussitôt.
    /// </summary>
    public static async Task SameTransactionAsync(TenantHarness harness, FakeRecipient recipient, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        await using (AsyncServiceScope abandoned = await harness.OpenAsync(dossier))
        {
            await Stage(abandoned, "cle-abandon");
        }

        Guid id;
        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().ListParkedAsync(default)).Should().BeEmpty();
            id = await Stage(scope, "cle-1");
            (await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(FakeRecipientHandler.Name, "cle-1", "{}", default))
                .Should().Be(id, "même clé dans la même transaction");
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);

            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchNowAsync(id, default))
                .Should().Be(OutboxOutcome.Delivered);
        }

        await using (AsyncServiceScope scope = await harness.OpenAsync(dossier))
        {
            IOutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            (await scope.ServiceProvider.GetRequiredService<ICustomerReadStore>().ListAsync(new PartyListFilter(null, 1, 50), default)).Items.Should().ContainSingle("le client abandonné n'a pas été écrit");
            (await dispatcher.FindAsync(id, default))!.DeliveredAt.Should().Be(Start);
            (await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(FakeRecipientHandler.Name, "cle-1", "{}", default))
                .Should().Be(id, "la clé est déjà en base");
            (await dispatcher.DispatchNowAsync(id, default)).Should().BeNull("déjà parti");
            (await dispatcher.DispatchDueAsync(default)).Should().Be(new OutboxPass(0, 0, 0));
        }

        recipient.Received.Should().ContainKey("cle-1").WhoseValue.Should().Be(1);
        recipient.Received.Should().NotContainKey("cle-abandon");

        static async Task<Guid> Stage(AsyncServiceScope scope, string key)
        {
            Customer customer = Customer.Create(CustomerId.New(), Martin, Start).Value;
            await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().AddAsync(customer, default);
            return await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(FakeRecipientHandler.Name, key, "{}", default);
        }
    }

    /// <summary>
    /// Destinataire injoignable : nouvel essai après 30 s, puis 1 min ; rien n'est repris avant l'heure. Un message qui
    /// échoue toujours est mis de côté au 3e essai, de même qu'un contenu refusé, tout de suite ; une exception vaut un
    /// échec passager. Un message mis de côté se remet dans la file.
    /// </summary>
    public static async Task RetryBackoffAndParkingAsync(TenantHarness harness, ManualClock clock, FakeRecipient recipient, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Guid flaky = await EnqueueAsync(harness, dossier, "cle-reprise", "{}");
        recipient.FailuresBeforeSuccess = 2;

        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 1, 0));
        OutboxMessageState state = (await FindAsync(harness, dossier, flaky))!;
        state.NextAttemptAt.Should().Be(Start.AddSeconds(30));
        state.LastError.Should().Be("Destinataire injoignable.");

        clock.Now = Start.AddSeconds(20);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 0, 0), "pas avant l'heure du prochain essai");

        clock.Now = Start.AddSeconds(30);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 1, 0));
        (await FindAsync(harness, dossier, flaky))!.NextAttemptAt.Should().Be(Start.AddSeconds(90), "l'attente double");

        clock.Now = Start.AddSeconds(90);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(1, 0, 0));
        recipient.Calls.Where(c => c.Key == "cle-reprise").Select(c => c.Attempt).Should().Equal(1, 2, 3);
        recipient.Received["cle-reprise"].Should().Be(1);

        Guid down = await EnqueueAsync(harness, dossier, "cle-toujours-en-panne", "throw");
        Guid refused = await EnqueueAsync(harness, dossier, "cle-refus", "poison");
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 1, 1));
        (await FindAsync(harness, dossier, refused))!.ParkedAt.Should().Be(clock.Now);
        (await FindAsync(harness, dossier, down))!.LastError.Should().Be(nameof(InvalidOperationException));
        await using (AsyncServiceScope peek = await harness.OpenAsync(dossier))
        {
            (await peek.ServiceProvider.GetRequiredService<IOutboxDispatcher>().GetBacklogAsync(default))
                .Should().Be(new OutboxBacklog(1, 1, (await FindAsync(harness, dossier, down))!.EnqueuedAt), "une reprise en attente, un refus mis de côté");
        }

        clock.Now = clock.Now.AddMinutes(1);
        await PassAsync(harness, dossier);
        clock.Now = clock.Now.AddMinutes(2);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 0, 1), "3e essai : mis de côté");

        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        IOutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        (await dispatcher.ListParkedAsync(default)).Select(m => m.IdempotencyKey).Should().BeEquivalentTo(["cle-refus", "cle-toujours-en-panne"]);
        (await dispatcher.GetBacklogAsync(default)).Should().Be(new OutboxBacklog(0, 2, null), "plus rien en attente");
        clock.Now = clock.Now.AddDays(1);
        (await dispatcher.DispatchDueAsync(default)).Should().Be(new OutboxPass(0, 0, 0), "mis de côté : plus d'essai");
        (await dispatcher.RequeueAsync(refused, default)).Should().BeTrue();
        (await dispatcher.RequeueAsync(flaky, default)).Should().BeFalse("parti, pas mis de côté");
        (await dispatcher.FindAsync(refused, default))!.Attempts.Should().Be(0);
    }

    /// <summary>
    /// Arrêt en plein envoi : le message est reçu mais le processus s'arrête avant le bilan. Le bail le garde jusqu'à
    /// son expiration, puis un autre répartiteur le reprend avec la même clé : le destinataire ne compte qu'un envoi.
    /// </summary>
    public static async Task CrashRecoveryAsync(TenantHarness harness, ManualClock clock, FakeRecipient recipient, DatabaseProvider provider, string connectionString)
    {
        DossierId dossier = harness.AddDossier(provider, connectionString);
        Guid id = await EnqueueAsync(harness, dossier, "cle-arret", "{}");

        using CancellationTokenSource crash = new();
        recipient.AfterReceive = crash.Cancel;
        Func<Task> crashed = () => PassAsync(harness, dossier, crash.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();
        recipient.AfterReceive = null;

        OutboxMessageState held = (await FindAsync(harness, dossier, id))!;
        held.DeliveredAt.Should().BeNull("le bilan n'a pas été écrit");
        held.Attempts.Should().Be(1);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(0, 0, 0), "le bail court encore");

        clock.Now = Start.AddMinutes(6);
        (await PassAsync(harness, dossier)).Should().Be(new OutboxPass(1, 0, 0));
        recipient.Calls.Should().Equal(("cle-arret", 1), ("cle-arret", 2));
        recipient.Received["cle-arret"].Should().Be(2, "le destinataire a vu deux fois la même clé");
        recipient.Received.Should().ContainSingle("une seule clé : il reconnaît le doublon");
    }

    /// <summary>
    /// Envoi immédiat désactivé (SaaS avec Worker) : la requête laisse le message au Worker ; la passe du Worker sur
    /// tous les dossiers actifs l'envoie.
    /// </summary>
    public static async Task WorkerOnlyAsync(TenantHarness harness, FakeRecipient recipient, DatabaseProvider provider, string first, string second)
    {
        DossierId a = harness.AddDossier(provider, first);
        DossierId b = harness.AddDossier(provider, second);
        Guid id = await EnqueueAsync(harness, a, "cle-a", "{}");
        await EnqueueAsync(harness, b, "cle-b", "{}");
        await using (AsyncServiceScope scope = await harness.OpenAsync(a))
        {
            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchNowAsync(id, default)).Should().BeNull();
        }

        OutboxDispatchJob job = new(
            harness.Services,
            harness.Services.GetRequiredService<IServiceScopeFactory>(),
            new OutboxDossierSource((_, _) => Task.FromResult<IReadOnlyList<TenantDescriptor>>([harness.Descriptor(a), harness.Descriptor(b)])),
            Options.Create(new OutboxDispatchJobOptions()),
            TimeProvider.System,
            NullLogger<OutboxDispatchJob>.Instance);
        (await job.RunPassAsync(default)).Should().Be(new OutboxPass(2, 0, 0));
        recipient.Received.Keys.Should().BeEquivalentTo(["cle-a", "cle-b"]);
        (await job.RunPassAsync(default)).Should().Be(new OutboxPass(0, 0, 0));
    }

    private static async Task<Guid> EnqueueAsync(TenantHarness harness, DossierId dossier, string key, string payload)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        Guid id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(FakeRecipientHandler.Name, key, payload, default);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return id;
    }

    private static async Task<OutboxPass> PassAsync(TenantHarness harness, DossierId dossier, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(cancellationToken);
    }

    private static async Task<OutboxMessageState?> FindAsync(TenantHarness harness, DossierId dossier, Guid id)
    {
        await using AsyncServiceScope scope = await harness.OpenAsync(dossier);
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().FindAsync(id, default);
    }
}
