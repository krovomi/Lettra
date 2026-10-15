using System.Text.Json;
using Lettra.Application;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Serialization;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Security;
using Lettra.Infrastructure.Persistence.Sqlite;
using Lettra.Infrastructure.Persistence.Sync;
using Lettra.Infrastructure.Persistence.Tenancy;
using Lettra.Sync.Client;
using Lettra.Sync.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>Horloge réglable des scénarios.</summary>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Droits réglables d'un utilisateur, par dossier (tous par défaut).</summary>
internal sealed class TestPermissions : IPermissionService
{
    public HashSet<Permission> Granted { get; } = [.. Enum.GetValues<Permission>()];

    public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlySet<Permission>>(Granted.ToHashSet());
}

/// <summary>
/// Une base de dossier SQLite réelle avec l'application complète (pipeline, cas d'usage, persistance) : le serveur du
/// cabinet ou la réplique d'un poste. Les deux portent le même identifiant de dossier, chacun dans son fichier.
/// </summary>
internal abstract class DossierNode : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    protected DossierNode(DossierId dossier, string file, TestClock clock, Action<IServiceCollection> configure)
    {
        Dossier = dossier;
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddApplication();
        services.AddPersistenceCore();
        services.AddSingleton<IDbProviderConfigurator>(new SqliteConfigurator());
        TenantDescriptor descriptor = new(dossier, new OrganisationId(Guid.CreateVersion7()), DatabaseProvider.Sqlite, "test:" + dossier.Value);
        services.AddSingleton<ITenantCatalog>(new Catalog(descriptor));
        services.AddSingleton<ISecretStore>(new Secrets($"Data Source={file};Pooling=False"));
        services.AddSingleton<ICurrentUser>(new User());
        services.AddSingleton<IPermissionService>(Permissions);
        services.AddSingleton<IKekStore>(new InMemoryKekStore());
        services.AddSingleton<IRequestedDossierSource>(new FixedDossier(dossier));
        configure(services);
        _root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public DossierId Dossier { get; }

    public TestPermissions Permissions { get; } = new();

    public IServiceProvider Services => _root;

    /// <summary>Une opération dans une portée sur le dossier, comme une requête HTTP ou un appel de la gateway in-process.</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        AsyncServiceScope scope = _root.CreateAsyncScope();
        await using (scope)
        {
            Result opened = await scope.ServiceProvider.GetRequiredService<ITenantInitializer>().InitializeAsync(default);
            opened.IsSuccess.Should().BeTrue();
            return await work(scope.ServiceProvider);
        }
    }

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command) =>
        RunAsync(sp => sp.GetRequiredService<ICommandDispatcher>().SendAsync(command, default));

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query) =>
        RunAsync(sp => sp.GetRequiredService<IQueryDispatcher>().QueryAsync(query, default));

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    private sealed class FixedDossier(DossierId dossier) : IRequestedDossierSource
    {
        public DossierId? RequestedDossier => dossier;
    }

    private sealed class Catalog(TenantDescriptor descriptor) : ITenantCatalog
    {
        public ValueTask<TenantDescriptor?> FindAsync(DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossierId == descriptor.DossierId ? descriptor : null);

        public ValueTask<bool> UserHasAccessAsync(Guid userId, DossierId dossierId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(dossierId == descriptor.DossierId);
    }

    private sealed class Secrets(string connectionString) : ISecretStore
    {
        public ValueTask<string> GetConnectionStringAsync(string secretRef, CancellationToken cancellationToken) =>
            ValueTask.FromResult(connectionString);
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid UserId { get; } = Guid.CreateVersion7();
    }
}

/// <summary>Le serveur : journal de synchronisation tenu, rejeu des intentions.</summary>
internal sealed class ServerNode(DossierId dossier, string file, TestClock clock)
    : DossierNode(dossier, file, clock, services => services.AddSyncServer().AddSyncJournal());

/// <summary>Où couper l'échange : la requête n'arrive pas, ou la réponse se perd après traitement.</summary>
public enum Cut
{
    PushRequest,
    PushResponse,
    PullRequest,
    PullResponse,
}

/// <summary>
/// Transport en mémoire vers le serveur. Chaque échange passe par le JSON des contrats, comme sur le réseau ;
/// <see cref="CutNext"/> fait échouer l'échange voulu une fois.
/// </summary>
internal sealed class InMemoryTransport(ServerNode server) : ISyncTransport
{
    public Cut? CutNext { get; set; }

    public int Pushes { get; private set; }

    public int Pulls { get; private set; }

    public async Task<PushSyncIntentsResponse> PushAsync(Guid dossierId, PushSyncIntentsRequest request, CancellationToken cancellationToken)
    {
        Pushes++;
        Lose(Cut.PushRequest);
        PushSyncIntentsRequest received = RoundTrip(request, ContractsJsonContext.Default.PushSyncIntentsRequest);
        Result<PushSyncIntentsResponse> result = await server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PushAsync(received, cancellationToken));
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
        Lose(Cut.PushResponse);
        return RoundTrip(result.Value, ContractsJsonContext.Default.PushSyncIntentsResponse);
    }

    public async Task<SyncChangesResponse> PullAsync(Guid dossierId, string? token, int limit, CancellationToken cancellationToken)
    {
        Pulls++;
        Lose(Cut.PullRequest);
        Result<SyncChangesResponse> result = await server.RunAsync(sp => sp.GetRequiredService<SyncServer>().PullAsync(token, limit, cancellationToken));
        if (result.IsFailure)
        {
            throw new SyncRefusedException(SyncFailure.Forbidden);
        }

        Lose(Cut.PullResponse);
        return RoundTrip(result.Value, ContractsJsonContext.Default.SyncChangesResponse);
    }

    private void Lose(Cut step)
    {
        if (CutNext == step)
        {
            CutNext = null;
            throw new HttpRequestException($"Coupure simulée : {step}.");
        }
    }

    private static T RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, typeInfo), typeInfo)!;
}

/// <summary>Demande à l'utilisateur jouée par le test : réponse choisie, et nombre de questions posées.</summary>
internal sealed class ScriptedConsent : ISyncConsentPrompt
{
    public SyncConsent Answer { get; set; } = SyncConsent.Now;

    public int Asked { get; private set; }

    public SyncSummary? LastSummary { get; private set; }

    public Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken cancellationToken)
    {
        Asked++;
        LastSummary = summary;
        return Task.FromResult(Answer);
    }

    public Task ShowDetailAsync(IReadOnlyList<SyncOutboxItem> pending, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class TestProbe : INetworkProbe
{
    public NetworkStatus Status { get; set; } = new(true, NetworkKind.Wifi);

    public Task<NetworkStatus> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Status);
}

internal sealed class TestPreference : ISyncPreferences
{
    public SyncPreference Current { get; set; } = SyncPreference.AlwaysAsk;
}

/// <summary>Un poste MAUI : réplique SQLite, outbox, coordinateur de synchronisation.</summary>
internal sealed class DeviceNode : DossierNode
{
    public DeviceNode(DossierId dossier, string file, TestClock clock, InMemoryTransport transport, int pageSize = 500)
        : base(dossier, file, clock, services => Configure(services, transport, pageSize))
    {
        Transport = transport;
    }

    public InMemoryTransport Transport { get; }

    public ScriptedConsent Consent => (ScriptedConsent)Services.GetRequiredService<ISyncConsentPrompt>();

    public TestProbe Probe => (TestProbe)Services.GetRequiredService<INetworkProbe>();

    public TestPreference Preference => (TestPreference)Services.GetRequiredService<ISyncPreferences>();

    public SyncCoordinator Coordinator => Services.GetRequiredService<SyncCoordinator>();

    public Task<SyncReport> SyncAsync() => Coordinator.RunAsync();

    public Task<IReadOnlyList<SyncOutboxItem>> OutboxAsync() =>
        RunAsync(sp => sp.GetRequiredService<ISyncOutbox>().ListAsync(null, default));

    public Task<IReadOnlyList<SyncConflictRecord>> ConflictsAsync() =>
        RunAsync(sp => sp.GetRequiredService<ISyncOutbox>().ListConflictsAsync(default));

    private static void Configure(IServiceCollection services, InMemoryTransport transport, int pageSize)
    {
        services.AddSingleton<ISyncConsentPrompt, ScriptedConsent>();
        services.AddSingleton<INetworkProbe, TestProbe>();
        services.AddSingleton<ISyncPreferences, TestPreference>();
        services.AddSingleton<ISyncTransport>(transport);
        services.AddSyncClient(o =>
        {
            o.DeviceId = Guid.CreateVersion7();
            o.BatchSize = 3;
            o.PageSize = pageSize;
        });
    }
}

/// <summary>Un serveur et des postes sur le même dossier, dans un répertoire temporaire.</summary>
internal sealed class SyncWorld : IAsyncDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lettra-sync-").FullName;
    private readonly List<DeviceNode> _devices = [];

    public SyncWorld()
    {
        Server = new ServerNode(Dossier, Path.Combine(_folder, "serveur.db"), Clock);
    }

    public DossierId Dossier { get; } = new(Guid.CreateVersion7());

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

    public ServerNode Server { get; }

    public DeviceNode Device(int pageSize = 500)
    {
        DeviceNode device = new(Dossier, Path.Combine(_folder, $"poste-{_devices.Count + 1}.db"), Clock, new InMemoryTransport(Server), pageSize);
        _devices.Add(device);
        return device;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (DeviceNode device in _devices)
        {
            await device.DisposeAsync();
        }

        await Server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
