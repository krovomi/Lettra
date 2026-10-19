using System.Net;
using System.Text;
using System.Text.Json;
using Lettra.Ai.Orchestration;
using Lettra.Ai.Skills;
using Lettra.Ai.Tools;
using Lettra.Ai.Tools.Company;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Ai.Tests;

/// <summary>Moteur scripté tour par tour, pour simuler un modèle qui se comporte mal (injection, hallucination…).</summary>
internal sealed class ScriptedTurnsEngine(params Func<EngineRequest, EngineTurn>[] turns) : IAiEngine
{
    private int _next;

    public string Name => "test:script";

    public int Calls => _next;

    public List<EngineRequest> Requests { get; } = [];

    public static EngineTurn Say(string text) => new([new TextPart(text)], EngineStopReason.EndTurn);

    public static EngineTurn Call(string tool, object input, string id = "c1") =>
        new([new ToolCallPart(id, tool, JsonSerializer.SerializeToElement(input))], EngineStopReason.ToolUse);

    public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken)
    {
        // Copie des messages : l'orchestrateur continue de remplir sa liste après l'appel.
        Requests.Add(request with { Messages = [.. request.Messages] });
        return Task.FromResult(turns[Math.Min(_next++, turns.Length - 1)](request));
    }

    /// <summary>Tout ce qui est parti vers le moteur, sauf la partie stable (surcouche et SKILL.md, qui ne contiennent aucune donnée).</summary>
    public string EverythingSentExceptStableInstructions() => string.Join('\n', Requests.SelectMany(r =>
        new[] { r.Context }.Concat(r.Messages.SelectMany(m => m.Parts).Select(p => p switch
        {
            TextPart t => t.Text,
            ToolCallPart c => c.Input.GetRawText(),
            ToolResultPart res => res.Content,
            _ => string.Empty,
        }))));
}

/// <summary>Faux serveur HTTP : enregistre les requêtes et renvoie une réponse fixe.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        return respond(request);
    }
}

/// <summary>Dossier d'exemple : identités connues du dossier, à ne jamais laisser partir en clair.</summary>
internal sealed class FakeDossierContext : IAiDossierContext
{
    public Task<string> GetCompanyJsonAsync(DossierId dossier, CancellationToken cancellationToken) =>
        Task.FromResult("""{"name":"WebCraft","siren":"912345678","siret":"91234567800014","tva_intracom":"FR40912345678","address":"12 rue du Commerce, 69003 Lyon","capital":1000,"tva_regime":"reel_normal"}""");

    public Task<IReadOnlyList<KnownIdentity>> GetKnownIdentitiesAsync(DossierId dossier, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KnownIdentity>>(
        [
            new(IdentityKind.Company, "WebCraft"),
            new(IdentityKind.Siren, "912345678"),
            new(IdentityKind.Siret, "91234567800014"),
            new(IdentityKind.VatNumber, "FR40912345678"),
            new(IdentityKind.Address, "12 rue du Commerce, 69003 Lyon"),
            new(IdentityKind.Client, "Dupont SARL"),
            new(IdentityKind.Siren, "987654321"),
            new(IdentityKind.Person, "Jean Martin"),
        ]);
}

/// <summary>Horloge fixe : le badge de fraîcheur du skill pack dépend de la date.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Monte la couche IA réelle (outils, orchestrateur, politique par configuration) autour d'un moteur scripté.</summary>
internal sealed class AssistantHarness : IAsyncDisposable
{
    public static readonly OrganisationId Organisation = new(Guid.Parse("0199e3a0-0000-7000-8000-0000000000c1"));
    public static readonly DossierId Dossier = new(Guid.Parse("0199e3a0-0000-7000-8000-0000000000d1"));
    public static readonly Guid UserId = Guid.Parse("0199e3a0-0000-7000-8000-0000000000a1");
    public static readonly IReadOnlySet<Permission> AllPermissions = Enum.GetValues<Permission>().ToHashSet();

    private readonly ServiceProvider _services;

    public AssistantHarness(
        ScriptedTurnsEngine engine,
        Dictionary<string, string?>? settings = null,
        HttpMessageHandler? webHandler = null,
        DateTimeOffset? now = null)
    {
        Engine = engine;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        ServiceCollection services = [];
        services.AddSingleton<TimeProvider>(new FixedTime(now ?? new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IAiSkillCatalog>(SkillCatalog.LoadEmbedded());
        services.AddAiTools();
        services.RemoveAll<IAiDossierContext>().AddScoped<IAiDossierContext, FakeDossierContext>();
        services.RemoveAll<ICompanyFactsSource>();

        // Pas de pipeline ici : les outils de la facturation parlent à un faux dispatcher qui enregistre chaque demande.
        services.AddSingleton(Dispatcher);
        services.AddSingleton<IQueryDispatcher>(Dispatcher);
        services.AddSingleton<ICommandDispatcher>(Dispatcher);
        services.AddSingleton<IAiAuditLog, InMemoryAiAuditLog>();
        services.AddSingleton<IAiSettingsStore>(Settings);
        services.AddAiOrchestration(configuration);
        services.AddKeyedSingleton<IAiEngine>(AiEngineKind.ClaudeEu, engine);
        if (webHandler is not null)
        {
            services.AddHttpClient(nameof(Lettra.Ai.Tools.Tools.OfficialWebLookupTool)).ConfigurePrimaryHttpMessageHandler(() => webHandler);
        }

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public ScriptedTurnsEngine Engine { get; }

    /// <summary>Faux dispatcher de l'application : ce que les outils ont demandé, et les réponses à leur faire.</summary>
    public RecordingDispatcher Dispatcher { get; } = new();

    /// <summary>Réglages enregistrés par cabinet (la base Catalog en production) ; vide par défaut.</summary>
    public InMemoryAiSettingsStore Settings { get; } = new();

    public static Dictionary<string, string?> Enabled(bool web = false, int? maxToolRounds = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Lettra:Ai:Enabled"] = "true",
            ["Lettra:Ai:Engine"] = nameof(AiEngineKind.ClaudeEu),
            ["Lettra:Ai:OfficialWebLookup"] = web ? "true" : "false",
        };
        AssistantFeature[] features = Enum.GetValues<AssistantFeature>();
        for (int i = 0; i < features.Length; i++)
        {
            settings[$"Lettra:Ai:Features:{i}"] = features[i].ToString();
        }

        if (maxToolRounds is { } rounds)
        {
            settings["Lettra:Ai:MaxToolRounds"] = rounds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return settings;
    }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public async Task<IReadOnlyList<AiAuditEntry>> AuditAsync() => await Get<IAiAuditLog>().ReadAsync(Dossier, 1000, CancellationToken.None);

    public async Task<List<AssistantEvent>> AskAsync(
        AssistantFeature feature,
        string question,
        string? imported = null,
        IReadOnlySet<Permission>? permissions = null,
        Guid? openDocumentId = null)
    {
        AssistantRequest request = new(
            Organisation, Dossier, UserId, permissions ?? AllPermissions, feature, question, OpenDocumentId: openDocumentId, ImportedContent: imported);
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        List<AssistantEvent> events = [];
        await foreach (AssistantEvent e in scope.ServiceProvider.GetRequiredService<IAccountingAssistant>().AskAsync(request, CancellationToken.None))
        {
            events.Add(e);
        }

        return events;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}

/// <summary>
/// Faux <see cref="IQueryDispatcher"/> et <see cref="ICommandDispatcher"/> : chaque demande est enregistrée, et la réponse
/// vient de la fonction déclarée pour son type. Une demande sans réponse déclarée fait échouer le test.
/// </summary>
internal sealed class RecordingDispatcher : IQueryDispatcher, ICommandDispatcher
{
    private readonly Dictionary<Type, Func<object, object>> _answers = [];

    public List<object> Sent { get; } = [];

    public RecordingDispatcher On<TRequest, TResult>(Func<TRequest, TResult> answer)
        where TResult : notnull
    {
        _answers[typeof(TRequest)] = request => answer((TRequest)request);
        return this;
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken) => Answer<TResult>(query);

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken) => Answer<TResult>(command);

    private Task<TResult> Answer<TResult>(object request)
    {
        Sent.Add(request);
        return _answers.TryGetValue(request.GetType(), out Func<object, object>? answer)
            ? Task.FromResult((TResult)answer(request))
            : throw new InvalidOperationException($"Demande inattendue : {request.GetType().Name}");
    }
}

/// <summary>Journal IA en mémoire : en production, c'est la table <c>AiJournal</c> de la base du dossier.</summary>
internal sealed class InMemoryAiAuditLog : IAiAuditLog
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<AiAuditEntry> _entries = new();

    public ValueTask AppendAsync(AiAuditEntry entry, CancellationToken cancellationToken)
    {
        _entries.Enqueue(entry);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AiAuditEntry>> ReadAsync(DossierId dossier, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AiAuditEntry>>([.. _entries.Where(e => e.Dossier == dossier).Reverse().Take(limit)]);
}

/// <summary>Réglages IA par cabinet en mémoire : en production, la table <c>AiSettings</c> de la base Catalog.</summary>
internal sealed class InMemoryAiSettingsStore : IAiSettingsStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<OrganisationId, StoredAiSettings> _rows = new();

    public bool IsAvailable => true;

    public ValueTask<StoredAiSettings?> FindAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_rows.TryGetValue(organisation, out StoredAiSettings? row) ? row : null);

    public ValueTask<Lettra.Domain.Common.Result<StoredAiSettings>> SaveAsync(
        OrganisationId organisation,
        AiSettings settings,
        long? expectedVersion,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        StoredAiSettings row = new(settings, (expectedVersion ?? 0) + 1, DateTimeOffset.UnixEpoch, updatedBy);
        _rows[organisation] = row;
        return ValueTask.FromResult<Lettra.Domain.Common.Result<StoredAiSettings>>(row);
    }
}
