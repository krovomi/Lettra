using System.Net;
using System.Text;
using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Composition;
using Lettra.Ai.Evals;
using Lettra.Ai.Orchestration;
using Lettra.Poc.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tests;

/// <summary>Moteur scripté tour par tour, pour simuler un modèle qui se comporte mal (injection, hallucination…).</summary>
internal sealed class ScriptedTurnsEngine(params Func<EngineRequest, EngineTurn>[] turns) : IAiEngine
{
    private int _next;
    public string Name => "test:script";
    public int Calls => _next;

    public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken ct = default) =>
        Task.FromResult(turns[Math.Min(_next++, turns.Length - 1)](request));

    public static EngineTurn Say(string text) => new([new TextPart(text)], EngineStopReason.EndTurn);

    public static EngineTurn Call(string tool, object input, string id = "c1") =>
        new([new ToolCallPart(id, tool, JsonSerializer.SerializeToElement(input))], EngineStopReason.ToolUse);
}

/// <summary>Faux serveur HTTP : enregistre les requêtes et renvoie une réponse fixe.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return respond(request);
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Monte l'assistant complet (composition du POC) autour d'un moteur donné.</summary>
internal sealed class AssistantHarness : IAsyncDisposable
{
    public static readonly Guid UserId = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
    public static readonly HashSet<string> AllPermissions = [AiPermissions.UseAssistant, AiPermissions.ReadInvoices, AiPermissions.DraftQuotes];

    private readonly ServiceProvider _sp;
    public SpyEngine Engine { get; }

    public AssistantHarness(IAiEngine engine, AiSettings? settings = null, HttpMessageHandler? webHandler = null)
    {
        Engine = new SpyEngine(engine);
        var services = new ServiceCollection().AddLettraAiPoc(new LettraAiPocOptions { EnableClaude = false, LocalEndpoint = null });
        services.AddSingleton<IAiEngineFactory>(new AiEngineFactory(new Dictionary<AiEngineKind, IAiEngine> { [AiEngineKind.Stub] = Engine }));
        if (webHandler is not null)
            services.AddHttpClient(nameof(Lettra.Ai.Tools.Tools.OfficialWebLookupTool)).ConfigurePrimaryHttpMessageHandler(() => webHandler);
        _sp = services.BuildServiceProvider();
        if (settings is not null) _sp.GetRequiredService<InMemoryAiUsagePolicy>().Set(SampleDossiers.CabinetId, settings);
    }

    public static AiSettings Enabled(bool web = false) => new()
    {
        Enabled = true, Engine = AiEngineKind.Stub, Features = Enum.GetValues<AiFeature>().ToHashSet(), OfficialWebLookup = web,
    };

    public T Get<T>() where T : notnull => _sp.GetRequiredService<T>();
    public InMemoryAiAuditLog Audit => (InMemoryAiAuditLog)Get<IAiAuditLog>();
    public Dossier Dossier => Get<InMemoryDossierStore>().Get(SampleDossiers.WebCraftId);

    public async Task<List<AssistantEvent>> AskAsync(AiFeature feature, string question, Guid? openInvoice = null,
        string? imported = null, IReadOnlySet<string>? permissions = null)
    {
        var request = new AssistantRequest(SampleDossiers.CabinetId, SampleDossiers.WebCraftId, UserId,
            permissions ?? AllPermissions, feature, question, OpenInvoiceId: openInvoice, ImportedContent: imported);
        var events = new List<AssistantEvent>();
        await foreach (var e in Get<IAccountingAssistant>().AskAsync(request)) events.Add(e);
        return events;
    }

    public ValueTask DisposeAsync() => _sp.DisposeAsync();
}
