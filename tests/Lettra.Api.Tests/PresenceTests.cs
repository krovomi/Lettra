using System.Threading.Channels;
using FluentAssertions;
using Lettra.Api.Presence;
using Lettra.Contracts.Common;
using Lettra.Contracts.Presence;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Présence SignalR de bout en bout : droits du dossier, diffusion au groupe du document, départ.</summary>
public sealed class PresenceHubTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly Guid Document = Guid.NewGuid();

    private HubConnection Connection(Guid userId) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, PresenceHubContract.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.Headers["Authorization"] = "Bearer " + LettraApiFactory.TokenFor(userId);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default))
            .Build();

    [Fact]
    public async Task Les_presents_sont_prevenus_des_arrivees_et_des_departs()
    {
        await using HubConnection manager = Connection(LettraApiFactory.ManagerId);
        Channel<DocumentPresence> received = Channel.CreateUnbounded<DocumentPresence>();
        manager.On<DocumentPresence>(PresenceHubContract.Changed, p => received.Writer.TryWrite(p));
        await manager.StartAsync();

        DocumentPresence alone = await manager.InvokeAsync<DocumentPresence>(
            PresenceHubContract.Join, LettraApiFactory.DossierId, PresenceDocumentType.Customer, Document, PresenceIntent.Editing);
        alone.Users.Should().ContainSingle(u => u.UserId == LettraApiFactory.ManagerId && u.Intent == PresenceIntent.Editing);

        HubConnection colleague = Connection(LettraApiFactory.CollaborateurId);
        await colleague.StartAsync();
        await colleague.InvokeAsync<DocumentPresence>(
            PresenceHubContract.Join, LettraApiFactory.DossierId, PresenceDocumentType.Customer, Document, PresenceIntent.Viewing);

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        DocumentPresence both = await received.Reader.ReadAsync(timeout.Token);
        both.Users.Select(u => u.UserId).Should().BeEquivalentTo([LettraApiFactory.ManagerId, LettraApiFactory.CollaborateurId]);

        await colleague.DisposeAsync();
        DocumentPresence after = await received.Reader.ReadAsync(timeout.Token);
        after.Users.Should().ContainSingle(u => u.UserId == LettraApiFactory.ManagerId);
    }

    [Fact]
    public async Task Sans_droit_de_consulter_le_document_la_presence_est_refusee()
    {
        await using HubConnection other = Connection(LettraApiFactory.AutreCabinetAdminId);
        await other.StartAsync();

        Func<Task> join = () => other.InvokeAsync<DocumentPresence>(
            PresenceHubContract.Join, LettraApiFactory.DossierId, PresenceDocumentType.Invoice, Document, PresenceIntent.Viewing);

        (await join.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain(ErrorCodes.Security.Forbidden);
    }

    [Fact]
    public async Task Le_hub_exige_une_authentification()
    {
        await using HubConnection anonymous = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, PresenceHubContract.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        Func<Task> start = () => anonymous.StartAsync();

        await start.Should().ThrowAsync<HttpRequestException>();
    }
}

/// <summary>Verrou souple : il expire sans signe de vie, et un utilisateur à plusieurs onglets ne compte qu'une fois.</summary>
public sealed class PresenceTrackerTests
{
    private static readonly PresenceKey Key = new(Guid.NewGuid(), PresenceDocumentType.Invoice, Guid.NewGuid());

    [Fact]
    public void Une_presence_sans_signe_de_vie_expire()
    {
        ManualClock clock = new();
        InMemoryPresenceTracker tracker = new(clock);
        tracker.Join(Key, "c1", Guid.NewGuid(), "Alice", PresenceIntent.Editing);

        clock.Advance(InMemoryPresenceTracker.Expiry - TimeSpan.FromSeconds(1));
        tracker.Get(Key).Users.Should().ContainSingle();

        clock.Advance(TimeSpan.FromSeconds(2));
        tracker.Get(Key).Users.Should().BeEmpty();
    }

    [Fact]
    public void Le_renouvellement_garde_la_presence_et_son_heure_d_arrivee()
    {
        ManualClock clock = new();
        InMemoryPresenceTracker tracker = new(clock);
        Guid user = Guid.NewGuid();
        DateTimeOffset arrival = tracker.Join(Key, "c1", user, "Alice", PresenceIntent.Viewing).Users[0].Since;

        clock.Advance(InMemoryPresenceTracker.Expiry - TimeSpan.FromSeconds(1));
        tracker.Join(Key, "c1", user, "Alice", PresenceIntent.Editing);
        clock.Advance(InMemoryPresenceTracker.Expiry - TimeSpan.FromSeconds(1));

        PresenceEntry entry = tracker.Get(Key).Users.Should().ContainSingle().Subject;
        entry.Since.Should().Be(arrival);
        entry.Intent.Should().Be(PresenceIntent.Editing);
    }

    [Fact]
    public void Plusieurs_onglets_d_un_utilisateur_font_une_entree_avec_l_intention_la_plus_forte()
    {
        InMemoryPresenceTracker tracker = new(new ManualClock());
        Guid user = Guid.NewGuid();
        tracker.Join(Key, "c1", user, "Alice", PresenceIntent.Viewing);
        tracker.Join(Key, "c2", user, "Alice", PresenceIntent.Editing);

        tracker.Get(Key).Users.Should().ContainSingle().Which.Intent.Should().Be(PresenceIntent.Editing);

        tracker.Disconnect("c2").Should().ContainSingle().Which.Users.Should().ContainSingle().Which.Intent.Should().Be(PresenceIntent.Viewing);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
