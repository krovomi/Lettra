using System.Net.Http;
using System.Text.Json;
using Anthropic;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Claude;
using Lettra.Ai.Local;

namespace Lettra.Ai.Tests;

/// <summary>Adapters de moteur testés contre un faux serveur HTTP : aucune clé d'API n'est nécessaire.</summary>
public sealed class EngineAdapterTests
{
    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { facture_id = new { type = "string" } },
        required = new[] { "facture_id" },
        additionalProperties = false,
    });

    private static EngineRequest Request(params EngineMessage[] messages) => new(
        "SURCOUCHE + SKILL.md",
        "Date du jour : 2026-10-14",
        messages.Length > 0 ? messages : [EngineMessage.UserText("Pourquoi ?")],
        [new EngineToolDefinition("valider_facture", "Vérifie la facture", Schema)]);

    private const string ClaudeToolUseResponse = """
    {
      "id": "msg_01", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
      "content": [
        { "type": "thinking", "thinking": "", "signature": "sig-123" },
        { "type": "text", "text": "Je vérifie." },
        { "type": "tool_use", "id": "toolu_01", "name": "valider_facture", "input": { "facture_id": "abc" } }
      ],
      "stop_reason": "tool_use", "stop_sequence": null,
      "usage": { "input_tokens": 120, "output_tokens": 30, "cache_read_input_tokens": 4000, "cache_creation_input_tokens": 0 }
    }
    """;

    private static (ClaudeEngine Engine, RecordingHandler Handler) Claude(string response)
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Json(response));
        var client = new AnthropicClient { ApiKey = "cle-de-test", HttpClient = new HttpClient(handler), MaxRetries = 0 };
        return (new ClaudeEngine(client, new ClaudeEngineOptions()), handler);
    }

    [Fact]
    public async Task Claude_met_le_prompt_stable_en_cache_et_declare_des_outils_stricts()
    {
        var (engine, handler) = Claude(ClaudeToolUseResponse);

        await engine.CompleteAsync(Request());

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var root = body.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal("medium", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.False(root.TryGetProperty("thinking", out _)); // adaptatif par défaut sur ce modèle

        var system = root.GetProperty("system");
        Assert.Equal("SURCOUCHE + SKILL.md", system[0].GetProperty("text").GetString());
        Assert.Equal("ephemeral", system[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.False(system[1].TryGetProperty("cache_control", out _));

        var tool = root.GetProperty("tools")[0];
        Assert.True(tool.GetProperty("strict").GetBoolean());
        Assert.False(tool.GetProperty("input_schema").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Claude_traduit_appels_d_outils_raisonnement_et_consommation()
    {
        var (engine, _) = Claude(ClaudeToolUseResponse);

        var turn = await engine.CompleteAsync(Request());

        Assert.Equal(EngineStopReason.ToolUse, turn.StopReason);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal(("toolu_01", "valider_facture", "abc"), (call.Id, call.Name, call.Input.GetProperty("facture_id").GetString()));
        Assert.Equal("Je vérifie.", turn.Text);
        Assert.Equal("sig-123", turn.Parts.OfType<OpaquePart>().Single().Payload.GetProperty("signature").GetString());
        Assert.Equal(4000, turn.Usage!.CacheReadTokens);
    }

    [Fact]
    public async Task Claude_renvoie_le_raisonnement_signe_et_les_resultats_d_outils_au_tour_suivant()
    {
        var (engine, handler) = Claude(ClaudeToolUseResponse);
        var first = await engine.CompleteAsync(Request());

        await engine.CompleteAsync(Request(
            EngineMessage.UserText("Pourquoi ?"),
            new EngineMessage(EngineRole.Assistant, first.Parts),
            new EngineMessage(EngineRole.User, [new ToolResultPart("toolu_01", "{\"statut\":\"NON CONFORME\"}", false)])));

        using var body = JsonDocument.Parse(handler.Requests[1].Body);
        var messages = body.RootElement.GetProperty("messages");
        var assistant = messages[1].GetProperty("content");
        Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
        Assert.Equal("sig-123", assistant[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", assistant[2].GetProperty("type").GetString());
        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_01", result.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task Claude_signale_un_refus()
    {
        var (engine, _) = Claude("""
        { "id": "msg_02", "type": "message", "role": "assistant", "model": "claude-opus-5-5", "content": [],
          "stop_reason": "refusal", "stop_sequence": null, "usage": { "input_tokens": 1, "output_tokens": 0 } }
        """);
        Assert.Equal(EngineStopReason.Refusal, (await engine.CompleteAsync(Request())).StopReason);
    }

    [Fact]
    public async Task Le_modele_local_recoit_les_outils_et_ses_appels_sont_traduits()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Json("""
        { "choices": [ { "finish_reason": "tool_calls", "message": { "role": "assistant", "content": null,
          "tool_calls": [ { "id": "call_1", "type": "function", "function": { "name": "valider_facture", "arguments": "{\"facture_id\":\"abc\"}" } },
                          { "id": "call_2", "type": "function", "function": { "name": "valider_facture", "arguments": "{pas du json" } } ] } } ],
          "usage": { "prompt_tokens": 50, "completion_tokens": 10 } }
        """));
        var engine = new LocalModelEngine(new HttpClient(handler), new LocalModelOptions { Endpoint = new Uri("http://modele.local/v1/chat/completions") });

        var turn = await engine.CompleteAsync(Request());

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("valider_facture", body.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(EngineStopReason.ToolUse, turn.StopReason);
        var calls = turn.ToolCalls.ToList();
        Assert.Equal("abc", calls[0].Input.GetProperty("facture_id").GetString());
        Assert.Equal(JsonValueKind.String, calls[1].Input.ValueKind); // JSON invalide : l'orchestrateur le refusera
    }
}
