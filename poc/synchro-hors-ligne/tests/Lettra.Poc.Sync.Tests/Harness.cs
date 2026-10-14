using Lettra.Poc.Sync.Client;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Poc.Sync.Tests;

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

public enum Fault { DropRequest, DropResponse, CaptivePortal }

/// <summary>
/// Simule le réseau : hors ligne complet, ou coupure ciblée sur la n-ième requête d'un chemin.
/// DropRequest : la requête n'arrive pas. DropResponse : le serveur a traité, la réponse se perd.
/// </summary>
public sealed class FlakyNetwork : DelegatingHandler
{
    private readonly List<(string Path, int Occurrence, Fault Fault)> _rules = [];
    private readonly Dictionary<string, int> _counts = [];

    public bool Online { get; set; } = true;
    public NetworkKind Kind { get; set; } = NetworkKind.Wifi;
    public List<string> Log { get; } = [];

    /// <summary>Coupe la n-ième requête vers ce chemin, comptée à partir de maintenant.</summary>
    public void CutOnce(string path, Fault fault, int occurrence = 1)
        => _rules.Add((path, _counts.GetValueOrDefault(path) + occurrence, fault));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!Online) throw new HttpRequestException("Pas de réseau.");

        _counts[path] = _counts.GetValueOrDefault(path) + 1;
        var rule = _rules.FirstOrDefault(r => path.StartsWith(r.Path, StringComparison.Ordinal) && r.Occurrence == _counts[path]);
        if (rule != default) _rules.Remove(rule);

        if (rule.Fault == Fault.CaptivePortal && rule != default)
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("<html>Connectez-vous au Wi-Fi de l'hôtel</html>", System.Text.Encoding.UTF8, "text/html") };
        if (rule.Fault == Fault.DropRequest && rule != default)
        {
            Log.Add($"{path} ✂ requête");
            throw new HttpRequestException("Coupure avant envoi.");
        }

        var response = await base.SendAsync(request, ct);
        if (rule.Fault == Fault.DropResponse && rule != default)
        {
            Log.Add($"{path} ✂ réponse");
            response.Dispose();
            throw new HttpRequestException("Coupure après traitement serveur.");
        }
        Log.Add(path);
        return response;
    }
}

public sealed class ScriptedConsent(params SyncConsent[] answers) : ISyncConsentPrompt
{
    private readonly Queue<SyncConsent> _answers = new(answers);
    public List<(SyncSummary Summary, NetworkKind Network)> Asked { get; } = [];
    public List<IReadOnlyList<OutboxItemView>> DetailsShown { get; } = [];

    public void Answer(params SyncConsent[] answers) { foreach (var a in answers) _answers.Enqueue(a); }

    public Task<SyncConsent> AskAsync(SyncSummary summary, NetworkKind network, CancellationToken ct)
    {
        Asked.Add((summary, network));
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : SyncConsent.Now);
    }

    public Task ShowDetailAsync(IReadOnlyList<OutboxItemView> items, CancellationToken ct)
    {
        DetailsShown.Add(items);
        return Task.CompletedTask;
    }
}

public sealed class SyncServerFactory(ManualClock clock) : WebApplicationFactory<Program>
{
    public ServerState State => Services.GetRequiredService<ServerState>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
}

/// <summary>Le serveur d'un dossier et ses postes, avec une horloge commune.</summary>
public sealed class World : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly List<TestDevice> _devices = [];

    public World()
    {
        Server = new SyncServerFactory(Clock);
        _ = Server.Server;   // démarre l'hôte
    }

    public ManualClock Clock { get; } = new(Start);
    public SyncServerFactory Server { get; }
    public ServerState State => Server.State;
    public Guid DossierId { get; } = Guid.NewGuid();

    public TestDevice Maui(string seriesCode, int batchSize = 50) => Add(ClientKind.Maui, seriesCode, batchSize);
    public TestDevice Browser() => Add(ClientKind.Browser, "WEB", 50);

    private TestDevice Add(ClientKind kind, string seriesCode, int batchSize)
    {
        var profile = new ClientProfile(Guid.NewGuid(), DossierId, kind, kind == ClientKind.Maui ? seriesCode : null);
        State.EnrollDevice(profile.DeviceId, seriesCode);
        var device = new TestDevice(this, profile, batchSize);
        device.Http.LoginAsync("pin-1234", default).GetAwaiter().GetResult();
        _devices.Add(device);
        return device;
    }

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        Server.Dispose();
    }
}

public sealed class TestDevice : IDisposable
{
    private readonly World _world;
    private readonly int _batchSize;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lettra-poc-{Guid.NewGuid():N}.db");

    public TestDevice(World world, ClientProfile profile, int batchSize)
    {
        _world = world;
        _batchSize = batchSize;
        Profile = profile;
        Network = new FlakyNetwork { InnerHandler = world.Server.Server.CreateHandler() };
        Build();
    }

    public ClientProfile Profile { get; }
    public FlakyNetwork Network { get; }
    public ScriptedConsent Consent { get; private set; } = new();
    public SyncPreference Preference { get; set; } = SyncPreference.AlwaysAsk;
    public LocalStore Store { get; private set; } = null!;
    public HttpSyncClient Http { get; private set; } = null!;
    public OfflineInvoicing Invoicing { get; private set; } = null!;
    public SyncCoordinator Sync { get; private set; } = null!;

    private void Build()
    {
        Store = new LocalStore(_path);
        var http = new HttpClient(Network, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        Http = new HttpSyncClient(http, Profile, Store, _world.Clock);
        Invoicing = new OfflineInvoicing(Profile, Store, _world.Clock);
        var probe = new HttpNetworkProbe(http, () => Network.Online ? Network.Kind : NetworkKind.None);
        Sync = new SyncCoordinator(Store, probe, Consent, Http, () => Preference, _world.Clock, _batchSize);
    }

    /// <summary>L'app est fermée puis rouverte : seule la base locale survit (jeton d'accès perdu).</summary>
    public void Restart()
    {
        Store.Dispose();
        Build();
    }

    public Task<SyncReport> ReconnectAsync()
    {
        Network.Online = true;
        return Sync.OnNetworkChangedAsync();
    }

    public void Dispose()
    {
        Store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }
}

public static class Lines
{
    public static InvoiceLineDto[] Audit(decimal price = 1200m) => [new("Mission de révision", 1, price, 0.20m)];
    public static InvoiceLineDto[] Two() => [new("Tenue comptable", 12, 150m, 0.20m), new("Bilan", 1, 900m, 0.20m)];
}
