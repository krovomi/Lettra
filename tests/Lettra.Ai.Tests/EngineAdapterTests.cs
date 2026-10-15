using System.Text.Json;
using Anthropic;
using Lettra.Ai.Claude;
using Lettra.Ai.Local;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tests;

/// <summary>Adapters de moteur testés contre un faux serveur HTTP : aucune clé d'API n'est nécessaire.</summary>
public sealed class EngineAdapterTests
{
    private const string ClaudeToolUseResponse = """
    {
      "id": "msg_01", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
      "content": [
        { "type": "thinking", "thinking": "", "signature": "sig-123" },
        { "type": "text", "text": "Je vérifie." },
        { "type": "tool_use", "id": "toolu_01", "name": "calculer", "input": { "operation": "cca" } }
      ],
      "stop_reason": "tool_use", "stop_sequence": null,
      "usage": { "input_tokens": 120, "output_tokens": 30, "cache_read_input_tokens": 4000, "cache_creation_input_tokens": 0 }
    }
    """;

    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { operation = new { type = "string" } },
        required = new[] { "operation" },
        additionalProperties = false,
    });

    [Fact]
    public async Task Claude_met_le_prompt_stable_en_cache_et_declare_des_outils_stricts()
    {
        (ClaudeEngine engine, RecordingHandler handler) = Claude(ClaudeToolUseResponse);

        await engine.CompleteAsync(Request(), CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Requests.Should().ContainSingle().Which.Body);
        JsonElement root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("claude-opus-5-5");
        root.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("medium");
        root.TryGetProperty("thinking", out _).Should().BeFalse("le raisonnement adaptatif est le défaut de ce modèle");

        JsonElement system = root.GetProperty("system");
        system[0].GetProperty("text").GetString().Should().Be("SURCOUCHE + SKILL.md");
        system[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        system[1].TryGetProperty("cache_control", out _).Should().BeFalse();

        JsonElement tool = root.GetProperty("tools")[0];
        tool.GetProperty("strict").GetBoolean().Should().BeTrue();
        tool.GetProperty("input_schema").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Claude_traduit_appels_d_outils_raisonnement_et_consommation()
    {
        (ClaudeEngine engine, _) = Claude(ClaudeToolUseResponse);

        EngineTurn turn = await engine.CompleteAsync(Request(), CancellationToken.None);

        turn.StopReason.Should().Be(EngineStopReason.ToolUse);
        ToolCallPart call = turn.ToolCalls.Should().ContainSingle().Which;
        (call.Id, call.Name, call.Input.GetProperty("operation").GetString()).Should().Be(("toolu_01", "calculer", "cca"));
        turn.Text.Should().Be("Je vérifie.");
        turn.Parts.OfType<OpaquePart>().Single().Payload.GetProperty("signature").GetString().Should().Be("sig-123");
        turn.Usage!.CacheReadTokens.Should().Be(4000);
    }

    [Fact]
    public async Task Claude_renvoie_le_raisonnement_signe_et_les_resultats_d_outils_au_tour_suivant()
    {
        (ClaudeEngine engine, RecordingHandler handler) = Claude(ClaudeToolUseResponse);
        EngineTurn first = await engine.CompleteAsync(Request(), CancellationToken.None);

        await engine.CompleteAsync(
            Request(
                EngineMessage.UserText("Pourquoi ?"),
                new EngineMessage(EngineRole.Assistant, first.Parts),
                new EngineMessage(EngineRole.User, [new ToolResultPart("toolu_01", "{\"CCA\":\"302,47 EUR\"}", false)])),
            CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Requests[1].Body);
        JsonElement messages = body.RootElement.GetProperty("messages");
        JsonElement assistant = messages[1].GetProperty("content");
        assistant[0].GetProperty("type").GetString().Should().Be("thinking");
        assistant[0].GetProperty("signature").GetString().Should().Be("sig-123");
        assistant[2].GetProperty("type").GetString().Should().Be("tool_use");
        JsonElement result = messages[2].GetProperty("content")[0];
        result.GetProperty("type").GetString().Should().Be("tool_result");
        result.GetProperty("tool_use_id").GetString().Should().Be("toolu_01");
    }

    [Fact]
    public async Task Claude_signale_un_refus()
    {
        (ClaudeEngine engine, _) = Claude("""
        { "id": "msg_02", "type": "message", "role": "assistant", "model": "claude-opus-5-5", "content": [],
          "stop_reason": "refusal", "stop_sequence": null, "usage": { "input_tokens": 1, "output_tokens": 0 } }
        """);

        (await engine.CompleteAsync(Request(), CancellationToken.None)).StopReason.Should().Be(EngineStopReason.Refusal);
    }

    [Fact]
    public async Task Le_modele_local_recoit_les_outils_et_ses_appels_sont_traduits()
    {
        RecordingHandler handler = new(_ => RecordingHandler.Json("""
        { "choices": [ { "finish_reason": "tool_calls", "message": { "role": "assistant", "content": null,
          "tool_calls": [ { "id": "call_1", "type": "function", "function": { "name": "calculer", "arguments": "{\"operation\":\"cca\"}" } },
                          { "id": "call_2", "type": "function", "function": { "name": "calculer", "arguments": "{pas du json" } } ] } } ],
          "usage": { "prompt_tokens": 50, "completion_tokens": 10 } }
        """));
        using HttpClient http = new(handler);
        LocalModelEngine engine = new(http, new LocalModelOptions { Endpoint = new Uri("http://modele.local/v1/chat/completions") });

        EngineTurn turn = await engine.CompleteAsync(Request(), CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Requests.Should().ContainSingle().Which.Body);
        body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("system");
        body.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString().Should().Be("calculer");
        turn.StopReason.Should().Be(EngineStopReason.ToolUse);
        List<ToolCallPart> calls = [.. turn.ToolCalls];
        calls[0].Input.GetProperty("operation").GetString().Should().Be("cca");
        calls[1].Input.ValueKind.Should().Be(JsonValueKind.String, "un JSON invalide passe tel quel et l'orchestrateur le refuse");
    }

    [Fact]
    public void Un_moteur_n_existe_que_si_sa_cle_ou_son_point_d_acces_est_configure()
    {
        static IAiEngine? Resolve(Dictionary<string, string?> settings, AiEngineKind kind)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            ServiceCollection services = new();
            services.AddClaudeEngine(configuration).AddLocalAiEngine(configuration);
            using ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetKeyedService<IAiEngine>(kind);
        }

        Resolve([], AiEngineKind.ClaudeEu).Should().BeNull();
        Resolve([], AiEngineKind.Local).Should().BeNull();
        const string EuEndpoint = "https://bedrock-runtime.eu-west-3.amazonaws.com";
        Resolve(new() { ["Lettra:Ai:Claude:ApiKey"] = "cle-de-test", ["Lettra:Ai:Claude:BaseUrl"] = EuEndpoint }, AiEngineKind.ClaudeEu)
            .Should().BeOfType<ClaudeEngine>();
        Resolve(new() { ["ANTHROPIC_API_KEY"] = "cle-de-test", ["Lettra:Ai:Claude:BaseUrl"] = EuEndpoint }, AiEngineKind.ClaudeEu)
            .Should().BeOfType<ClaudeEngine>();
        Resolve(new() { ["Lettra:Ai:Local:Endpoint"] = "http://modele.local/v1/chat/completions" }, AiEngineKind.Local)
            .Should().BeOfType<LocalModelEngine>().Which.Name.Should().Be("local:mistral-small");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://api.anthropic.com")]
    [InlineData("https://API.Anthropic.com/v1")]
    [InlineData("http://bedrock-runtime.eu-west-3.amazonaws.com")]
    public void Sans_point_d_acces_en_region_UE_le_moteur_Claude_refuse_de_demarrer(string? baseUrl)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = "cle-de-test",
            ["Lettra:Ai:Claude:BaseUrl"] = baseUrl,
        }).Build();

        FluentActions.Invoking(() => new ServiceCollection().AddClaudeEngine(configuration))
            .Should().Throw<InvalidOperationException>().WithMessage("*région UE*");
    }

    [Fact]
    public void L_API_directe_n_est_acceptee_que_sur_autorisation_explicite_pour_les_evaluations()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = "cle-de-test",
            ["Lettra:Ai:Claude:AllowNonEuEndpoint"] = "true",
        }).Build();
        ServiceCollection services = new();
        services.AddClaudeEngine(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetKeyedService<IAiEngine>(AiEngineKind.ClaudeEu).Should().BeOfType<ClaudeEngine>();
    }

    private static EngineRequest Request(params EngineMessage[] messages) => new(
        "SURCOUCHE + SKILL.md",
        "Date du jour : 2026-10-14",
        messages.Length > 0 ? messages : [EngineMessage.UserText("Pourquoi ?")],
        [new EngineToolDefinition("calculer", "Calcul comptable déterministe", Schema)]);

    private static (ClaudeEngine Engine, RecordingHandler Handler) Claude(string response)
    {
        RecordingHandler handler = new(_ => RecordingHandler.Json(response));
        AnthropicClient client = new() { ApiKey = "cle-de-test", HttpClient = new HttpClient(handler), MaxRetries = 0 };
        return (new ClaudeEngine(client, new ClaudeEngineOptions()), handler);
    }
}
