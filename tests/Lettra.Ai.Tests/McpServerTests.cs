using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tests;

/// <summary>
/// Le serveur MCP : il n'ouvre que les outils en lecture seule de l'assistant du poste que les droits permettent, ne laisse
/// sortir aucun nom en clair, rétablit les jetons qui reviennent, et écrit chaque connexion, appel et refus au journal IA.
/// </summary>
public sealed class McpServerTests : IAsyncDisposable
{
    private static readonly AiSettings ExternalOnly = new() { Enabled = true, Features = new HashSet<AssistantFeature> { AssistantFeature.ExternalAssistant } };

    private readonly ClientsTool _tool = new();
    private readonly AssistantHarness _harness;

    public McpServerTests() =>
        _harness = new AssistantHarness(new ScriptedTurnsEngine(_ => ScriptedTurnsEngine.Say("inutilisé")), configure: services => services.AddSingleton<IAiTool>(_tool));

    private static McpCaller Caller(IReadOnlySet<Permission>? permissions = null, Guid? user = null) =>
        new(AssistantHarness.Organisation, AssistantHarness.Dossier, user ?? AssistantHarness.UserId, permissions ?? AssistantHarness.AllPermissions, ExternalOnly);

    private async Task<McpReply> SendAsync(string? session, string json, McpCaller? caller = null)
    {
        await using AsyncServiceScope scope = _harness.Get<IServiceScopeFactory>().CreateAsyncScope();
        using JsonDocument message = JsonDocument.Parse(json);
        return await scope.ServiceProvider.GetRequiredService<IMcpServer>().HandleAsync(session, message.RootElement, caller ?? Caller(), CancellationToken.None);
    }

    private async Task<string> OpenAsync(McpCaller? caller = null)
    {
        McpReply reply = await SendAsync(
            null,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"Poste de Jean Martin"}}}""",
            caller);
        reply.Status.Should().Be(200);
        return reply.SessionId!;
    }

    private static JsonNode Result(McpReply reply) => JsonNode.Parse(reply.Json!)!["result"]!;

    private static string CallText(McpReply reply) => Result(reply)["content"]![0]!["text"]!.GetValue<string>();

    [Fact]
    public async Task Initialize_ouvre_une_session_dans_la_version_demandee_et_le_journalise_sans_nom()
    {
        McpReply reply = await SendAsync(
            null,
            """{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"Poste de Jean Martin"}}}""");

        reply.Status.Should().Be(200);
        reply.SessionId.Should().NotBeNullOrEmpty();
        JsonNode result = Result(reply);
        result["protocolVersion"]!.GetValue<string>().Should().Be("2025-03-26");
        result["capabilities"]!["tools"].Should().NotBeNull();
        JsonNode.Parse(reply.Json!)!["id"]!.GetValue<string>().Should().Be("a");

        AiAuditEntry entry = (await _harness.AuditAsync()).Should().ContainSingle().Subject;
        (entry.Kind, entry.Engine).Should().Be((AiAuditKind.Request, "mcp"));
        entry.Detail.Should().NotContain("Jean Martin");
    }

    [Fact]
    public async Task Une_version_inconnue_recoit_la_plus_recente()
    {
        McpReply reply = await SendAsync(null, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        Result(reply)["protocolVersion"]!.GetValue<string>().Should().Be("2025-06-18");
    }

    [Fact]
    public async Task La_liste_ne_montre_que_les_outils_de_l_assistant_du_poste_permis_par_les_droits()
    {
        string session = await OpenAsync();

        McpReply all = await SendAsync(session, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        McpReply none = await SendAsync(session, """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""", Caller(new HashSet<Permission> { Permission.IaUtiliser }));

        JsonArray tools = Result(all)["tools"]!.AsArray();
        // Les outils communs sans donnée du dossier (calculatrice, références réglementaires) sont proposés aussi.
        tools.Select(t => t!["name"]!.GetValue<string>()).Should().BeEquivalentTo(ClientsTool.ToolName, "calculer", "lire_reference", "lister_references");
        tools.Should().OnlyContain(t => t!["annotations"]!["readOnlyHint"]!.GetValue<bool>() && t["inputSchema"]!["type"]!.GetValue<string>() == "object");
        Result(none)["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).Should().NotContain(ClientsTool.ToolName, "sans DossierLire, l'outil n'est pas proposé");
    }

    [Fact]
    public async Task Un_appel_ne_laisse_sortir_que_des_jetons_et_les_jetons_qui_reviennent_sont_retablis()
    {
        string session = await OpenAsync();

        string first = CallText(await SendAsync(session, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"lire_clients"}}"""));
        string token = JsonNode.Parse(first)!["clients"]![0]!["nom"]!.GetValue<string>();
        McpReply second = await SendAsync(session, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 5,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = "lire_clients", ["arguments"] = new JsonObject { ["nom"] = token } },
        }.ToJsonString());

        first.Should().NotContain("Dupont SARL").And.NotContain("Jean Martin");
        token.Should().StartWith("[CLIENT_");
        _tool.Received.Should().Equal(null, "Dupont SARL");
        Result(second)["isError"]!.GetValue<bool>().Should().BeFalse();
        CallText(second).Should().NotContain("Dupont SARL");

        IReadOnlyList<AiAuditEntry> journal = await _harness.AuditAsync();
        journal.Where(e => e.Kind == AiAuditKind.ToolCall).Should().HaveCount(2);
        journal.Should().OnlyContain(e => !e.Detail.Contains("Dupont SARL", StringComparison.Ordinal) && e.Engine == "mcp");
    }

    [Fact]
    public async Task Un_nom_en_clair_ecrit_par_l_assistant_est_pseudonymise_dans_le_journal()
    {
        string session = await OpenAsync();

        await SendAsync(session, """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"lire_clients","arguments":{"nom":"Dupont SARL"}}}""");

        AiAuditEntry call = (await _harness.AuditAsync()).Single(e => e.Kind == AiAuditKind.ToolCall);
        call.Detail.Should().StartWith("lire_clients").And.NotContain("Dupont SARL");
    }

    [Fact]
    public async Task Un_outil_inconnu_ou_hors_droits_est_refuse_et_journalise()
    {
        string session = await OpenAsync();

        McpReply unknown = await SendAsync(session, """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"supprimer_facture"}}""");
        McpReply forbidden = await SendAsync(
            session,
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"lire_clients"}}""",
            Caller(new HashSet<Permission> { Permission.IaUtiliser }));

        JsonNode.Parse(unknown.Json!)!["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        JsonNode.Parse(forbidden.Json!)!["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
        _tool.Received.Should().BeEmpty();
        (await _harness.AuditAsync()).Count(e => e.Kind == AiAuditKind.ToolRejected).Should().Be(2);
    }

    [Fact]
    public async Task Une_entree_hors_schema_est_refusee_sans_appeler_l_outil()
    {
        string session = await OpenAsync();

        McpReply reply = await SendAsync(session, """{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"lire_clients","arguments":{"montant":12}}}""");

        Result(reply)["isError"]!.GetValue<bool>().Should().BeTrue();
        _tool.Received.Should().BeEmpty();
        (await _harness.AuditAsync()).Should().Contain(e => e.Kind == AiAuditKind.ToolRejected);
    }

    [Fact]
    public async Task Une_session_inconnue_ou_d_un_autre_utilisateur_repond_404()
    {
        string session = await OpenAsync();

        (await SendAsync(null, """{"jsonrpc":"2.0","id":10,"method":"tools/list"}""")).Status.Should().Be(404);
        (await SendAsync("inconnue", """{"jsonrpc":"2.0","id":11,"method":"ping"}""")).Status.Should().Be(404);
        (await SendAsync(session, """{"jsonrpc":"2.0","id":12,"method":"ping"}""", Caller(user: Guid.CreateVersion7()))).Status.Should().Be(404);
        (await SendAsync(session, """{"jsonrpc":"2.0","id":13,"method":"ping"}""")).Status.Should().Be(200);
    }

    [Fact]
    public async Task Une_session_inactive_trente_minutes_est_oubliee()
    {
        string session = await OpenAsync();
        ((FixedTime)_harness.Get<TimeProvider>()).Advance(TimeSpan.FromMinutes(31));

        (await SendAsync(session, """{"jsonrpc":"2.0","id":14,"method":"ping"}""")).Status.Should().Be(404);
    }

    [Fact]
    public async Task Notifications_methodes_inconnues_et_messages_invalides()
    {
        string session = await OpenAsync();

        (await SendAsync(session, """{"jsonrpc":"2.0","method":"notifications/initialized"}""")).Status.Should().Be(202);
        McpReply unknown = await SendAsync(session, """{"jsonrpc":"2.0","id":15,"method":"resources/list"}""");
        JsonNode.Parse(unknown.Json!)!["error"]!["code"]!.GetValue<int>().Should().Be(-32601);
        (await SendAsync(session, """[{"jsonrpc":"2.0","id":16,"method":"ping"}]""")).Status.Should().Be(400);
        (await SendAsync(session, """{"id":17,"method":"ping"}""")).Status.Should().Be(400);
    }

    [Fact]
    public async Task Fermer_la_session_oublie_ses_pseudonymes()
    {
        string session = await OpenAsync();
        using IServiceScope scope = _harness.Get<IServiceScopeFactory>().CreateScope();
        IMcpServer server = scope.ServiceProvider.GetRequiredService<IMcpServer>();

        server.End(session, Caller(user: Guid.CreateVersion7())).Should().BeFalse("seul son utilisateur peut la fermer");
        server.End(session, Caller()).Should().BeTrue();
        server.End(session, Caller()).Should().BeFalse();
        (await SendAsync(session, """{"jsonrpc":"2.0","id":18,"method":"ping"}""")).Status.Should().Be(404);
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    /// <summary>Outil de test de l'assistant du poste : rend un nom de client en clair et note le paramètre reçu.</summary>
    private sealed class ClientsTool : IAiTool
    {
        public const string ToolName = "lire_clients";

        public List<string?> Received { get; } = [];

        public string Name => ToolName;

        public string Description => "Clients du dossier.";

        public JsonElement InputSchema { get; } = JsonDocument.Parse(
            """{"type":"object","properties":{"nom":{"type":"string"}},"additionalProperties":false}""").RootElement.Clone();

        public AssistantToolRisk Risk => AssistantToolRisk.ReadOnly;

        public Permission? RequiredPermission => Permission.DossierLire;

        public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.ExternalAssistant };

        public bool ReceivesRealValues => true;

        public bool IsEnabledBy(AiSettings settings) => true;

        public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken)
        {
            Received.Add(input.TryGetProperty("nom", out JsonElement nom) ? nom.GetString() : null);
            return Task.FromResult(new AiToolResult("""{"clients":[{"nom":"Dupont SARL","contact":"Jean Martin","du":1200}]}"""));
        }
    }
}
