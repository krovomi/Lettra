using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Local;

/// <summary>Réglages du modèle local (<c>Lettra:Ai:Local</c>).</summary>
public sealed class LocalModelOptions
{
    public const string SectionName = "Lettra:Ai:Local";

    /// <summary>Point d'accès compatible « chat completions » (vLLM, Ollama, TGI…). Vide : moteur local indisponible.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Modèle à poids ouverts, par exemple un Mistral servi localement.</summary>
    public string Model { get; set; } = "mistral-small";

    public int MaxTokens { get; set; } = 4_000;
}

/// <summary>
/// Adapter « modèle souverain » (ADR-0012) : modèle à poids ouverts servi par une API compatible chat completions.
/// Mêmes outils, même orchestrateur, même banc d'évals que Claude ; la qualité se mesure, elle ne se suppose pas.
/// </summary>
public sealed class LocalModelEngine(HttpClient http, LocalModelOptions options) : IAiEngine
{
    public string Name => $"local:{options.Model}";

    public async Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http
            .PostAsJsonAsync(options.Endpoint, BuildBody(request), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        return ToTurn(body);
    }

    public JsonObject BuildBody(EngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonArray messages = [new JsonObject { ["role"] = "system", ["content"] = request.StableInstructions + "\n\n" + request.Context }];
        foreach (EngineMessage message in request.Messages)
        {
            if (message.Role == EngineRole.Assistant)
            {
                messages.Add(AssistantMessage(message));
                continue;
            }

            foreach (EnginePart part in message.Parts)
            {
                switch (part)
                {
                    case ToolResultPart result:
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = result.ToolCallId,
                            ["content"] = result.IsError ? "ERREUR : " + result.Content : result.Content,
                        });
                        break;
                    case TextPart text:
                        messages.Add(new JsonObject { ["role"] = "user", ["content"] = text.Text });
                        break;
                    default:
                        // Parties sans équivalent (raisonnement propre à un autre moteur) : ignorées.
                        break;
                }
            }
        }

        return new JsonObject
        {
            ["model"] = options.Model,
            ["max_tokens"] = options.MaxTokens,
            ["temperature"] = 0,
            ["messages"] = messages,
            ["tools"] = new JsonArray([.. request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                },
            })]),
            ["tool_choice"] = "auto",
        };
    }

    private static JsonObject AssistantMessage(EngineMessage message)
    {
        string text = string.Concat(message.Parts.OfType<TextPart>().Select(p => p.Text));
        JsonArray calls = [.. message.Parts.OfType<ToolCallPart>().Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id,
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Input.GetRawText() },
        })];
        JsonObject result = new() { ["role"] = "assistant", ["content"] = text.Length > 0 ? text : null };
        if (calls.Count > 0)
        {
            result["tool_calls"] = calls;
        }

        return result;
    }

    private static EngineTurn ToTurn(JsonElement body)
    {
        JsonElement choice = body.GetProperty("choices")[0];
        JsonElement message = choice.GetProperty("message");
        List<EnginePart> parts = [];
        if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
        {
            parts.Add(new TextPart(text));
        }

        if (message.TryGetProperty("tool_calls", out JsonElement calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement call in calls.EnumerateArray())
            {
                JsonElement function = call.GetProperty("function");
                parts.Add(new ToolCallPart(
                    call.TryGetProperty("id", out JsonElement id) ? id.GetString()! : Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                    function.GetProperty("name").GetString()!,
                    ParseArguments(function.GetProperty("arguments"))));
            }
        }

        string? finish = choice.TryGetProperty("finish_reason", out JsonElement f) ? f.GetString() : null;
        EngineStopReason stop = finish switch
        {
            "tool_calls" => EngineStopReason.ToolUse,
            "length" => EngineStopReason.MaxTokens,
            "content_filter" => EngineStopReason.Refusal,
            _ when parts.OfType<ToolCallPart>().Any() => EngineStopReason.ToolUse,
            _ => EngineStopReason.EndTurn,
        };

        EngineUsage? usage = body.TryGetProperty("usage", out JsonElement u)
            ? new EngineUsage(Long(u, "prompt_tokens"), Long(u, "completion_tokens"), 0, 0)
            : null;
        return new EngineTurn(parts, stop, usage);
    }

    /// <summary>Les arguments arrivent en texte JSON ; s'ils sont invalides, ils passent tels quels et l'orchestrateur les refuse.</summary>
    private static JsonElement ParseArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.String)
        {
            return arguments.Clone();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(arguments.GetString()!);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return arguments.Clone();
        }
    }

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number) ? number : 0;
}

public static class DependencyInjection
{
    /// <summary>Enregistre le moteur local, seulement si un point d'accès est configuré sur ce déploiement.</summary>
    public static IServiceCollection AddLocalAiEngine(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        LocalModelOptions options = configuration.GetSection(LocalModelOptions.SectionName).Get<LocalModelOptions>() ?? new();
        if (options.Endpoint is null)
        {
            return services;
        }

        services.AddHttpClient(nameof(LocalModelEngine), client => client.Timeout = TimeSpan.FromMinutes(2));
        services.AddKeyedSingleton<IAiEngine>(AiEngineKind.Local, (sp, _) =>
            new LocalModelEngine(sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LocalModelEngine)), options));
        return services;
    }
}
