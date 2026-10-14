using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Local;

public sealed record LocalModelOptions
{
    /// <summary>Point d'accès compatible « chat completions » (vLLM, Ollama, TGI…) hébergé en France ou chez le cabinet.</summary>
    public Uri Endpoint { get; init; } = new("http://localhost:8000/v1/chat/completions");
    /// <summary>Modèle à poids ouverts, par exemple un Mistral servi localement.</summary>
    public string Model { get; init; } = "mistral-small";
    public int MaxTokens { get; init; } = 4_000;
}

/// <summary>
/// Adapter « modèle souverain » (ADR-0012) : modèle à poids ouverts servi par une API compatible chat completions.
/// Mêmes outils, même orchestrateur, même banc d'évals que Claude ; la qualité se mesure, elle ne se suppose pas.
/// </summary>
public sealed class LocalModelEngine(HttpClient http, LocalModelOptions options) : IAiEngine
{
    public string Name => $"local:{options.Model}";

    public async Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(options.Endpoint, BuildBody(request), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return ToTurn(body);
    }

    public JsonObject BuildBody(EngineRequest request)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.StableInstructions + "\n\n" + request.Context },
        };
        foreach (var message in request.Messages)
        {
            if (message.Role == EngineRole.Assistant)
            {
                var text = string.Concat(message.Parts.OfType<TextPart>().Select(p => p.Text));
                var calls = new JsonArray(message.Parts.OfType<ToolCallPart>().Select(c => (JsonNode)new JsonObject
                {
                    ["id"] = c.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Input.GetRawText() },
                }).ToArray());
                var m = new JsonObject { ["role"] = "assistant", ["content"] = text.Length > 0 ? text : null };
                if (calls.Count > 0) m["tool_calls"] = calls;
                messages.Add(m);
                continue;
            }
            foreach (var part in message.Parts)
            {
                messages.Add(part switch
                {
                    ToolResultPart r => new JsonObject
                    {
                        ["role"] = "tool", ["tool_call_id"] = r.ToolCallId,
                        ["content"] = r.IsError ? "ERREUR : " + r.Content : r.Content,
                    },
                    TextPart t => new JsonObject { ["role"] = "user", ["content"] = t.Text },
                    _ => null,
                });
            }
        }
        // Les parties sans équivalent (raisonnement propre à un autre moteur) sont ignorées.
        for (var i = messages.Count - 1; i >= 0; i--) if (messages[i] is null) messages.RemoveAt(i);

        return new JsonObject
        {
            ["model"] = options.Model,
            ["max_tokens"] = options.MaxTokens,
            ["temperature"] = 0,
            ["messages"] = messages,
            ["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                },
            }).ToArray()),
            ["tool_choice"] = "auto",
        };
    }

    private static EngineTurn ToTurn(JsonElement body)
    {
        var choice = body.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var parts = new List<EnginePart>();
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
            parts.Add(new TextPart(text));
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var arguments = function.GetProperty("arguments");
                parts.Add(new ToolCallPart(
                    call.TryGetProperty("id", out var id) ? id.GetString()! : Guid.NewGuid().ToString("N"),
                    function.GetProperty("name").GetString()!,
                    ParseArguments(arguments)));
            }
        }

        var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
        var stop = finish switch
        {
            "tool_calls" => EngineStopReason.ToolUse,
            "length" => EngineStopReason.MaxTokens,
            "content_filter" => EngineStopReason.Refusal,
            _ when parts.OfType<ToolCallPart>().Any() => EngineStopReason.ToolUse,
            _ => EngineStopReason.EndTurn,
        };

        EngineUsage? usage = body.TryGetProperty("usage", out var u)
            ? new EngineUsage(Long(u, "prompt_tokens"), Long(u, "completion_tokens"), 0, 0)
            : null;
        return new EngineTurn(parts, stop, usage);
    }

    /// <summary>Les arguments arrivent en texte JSON ; s'ils sont invalides, on les transmet tels quels et l'orchestrateur les refuse.</summary>
    private static JsonElement ParseArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.String) return arguments.Clone();
        try { return JsonDocument.Parse(arguments.GetString()!).RootElement.Clone(); }
        catch (JsonException) { return arguments.Clone(); }
    }

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var l) ? l : 0;
}
