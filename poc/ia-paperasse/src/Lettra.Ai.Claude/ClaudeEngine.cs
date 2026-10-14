using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lettra.Ai.Abstractions;

namespace Lettra.Ai.Claude;

public sealed record ClaudeEngineOptions
{
    /// <summary>Modèle Claude. À régler par fonction après mesure (annexe B §5.4).</summary>
    public string Model { get; init; } = "claude-opus-5-5";
    public int MaxTokens { get; init; } = 16_000;
    /// <summary>Effort de raisonnement ; « medium » est le défaut de ce modèle, rendu explicite ici.</summary>
    public Effort Effort { get; init; } = Effort.Medium;
}

/// <summary>
/// Adapter Claude (SDK officiel C# « Anthropic »), derrière le port <see cref="IAiEngine"/>. L'orchestrateur garde
/// la boucle d'outils : on n'utilise pas le tool runner du SDK, pour que chaque résultat d'outil soit pseudonymisé
/// et contrôlé côté serveur avant de repartir. Le client fourni désigne le point d'accès choisi par le cabinet
/// (API directe avec résidence UE, Bedrock ou Vertex en région européenne).
/// </summary>
public sealed class ClaudeEngine(IAnthropicClient client, ClaudeEngineOptions options) : IAiEngine
{
    public const string EngineName = "claude";

    public string Name => $"{EngineName}:{options.Model}";

    public async Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken ct = default)
    {
        var parameters = BuildParameters(request);
        var response = await client.Messages.Create(parameters, ct);
        return ToTurn(response);
    }

    /// <summary>Construit la requête : partie stable du prompt en cache, partie variable ensuite, outils stricts.</summary>
    public MessageCreateParams BuildParameters(EngineRequest request) => new()
    {
        Model = options.Model,
        MaxTokens = options.MaxTokens,
        OutputConfig = new OutputConfig { Effort = options.Effort },
        System = new List<TextBlockParam>
        {
            // Surcouche Lettra + SKILL.md : identiques d'une requête à l'autre, donc mis en cache.
            new() { Text = request.StableInstructions, CacheControl = new CacheControlEphemeral() },
            // Contexte du dossier (pseudonymisé) : variable, après le point de cache.
            new() { Text = request.Context },
        },
        Tools = request.Tools.Select(t => (ToolUnion)new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = InputSchema.FromRawUnchecked(ToDictionary(t.InputSchema)),
            Strict = true,
        }).ToList(),
        Messages = request.Messages.Select(ToParam).ToList(),
    };

    private static MessageParam ToParam(EngineMessage message)
    {
        var blocks = new List<ContentBlockParam>();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case TextPart t:
                    blocks.Add(new TextBlockParam { Text = t.Text });
                    break;
                case ToolCallPart call:
                    blocks.Add(new ToolUseBlockParam { ID = call.Id, Name = call.Name, Input = ToDictionary(call.Input) });
                    break;
                case ToolResultPart result:
                    blocks.Add(new ToolResultBlockParam { ToolUseID = result.ToolCallId, Content = result.Content, IsError = result.IsError });
                    break;
                case OpaquePart { Engine: EngineName } opaque when opaque.Payload.GetProperty("type").GetString() == "thinking":
                    // Le raisonnement signé doit revenir intact au même modèle.
                    blocks.Add(new ThinkingBlockParam
                    {
                        Thinking = opaque.Payload.GetProperty("thinking").GetString()!,
                        Signature = opaque.Payload.GetProperty("signature").GetString()!,
                    });
                    break;
                case OpaquePart { Engine: EngineName } opaque when opaque.Payload.GetProperty("type").GetString() == "redacted_thinking":
                    blocks.Add(new RedactedThinkingBlockParam { Data = opaque.Payload.GetProperty("data").GetString()! });
                    break;
            }
        }
        return new MessageParam { Role = message.Role == EngineRole.User ? Role.User : Role.Assistant, Content = blocks };
    }

    private static EngineTurn ToTurn(Message response)
    {
        var parts = new List<EnginePart>();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text)) parts.Add(new TextPart(text.Text));
            else if (block.TryPickToolUse(out var toolUse))
                parts.Add(new ToolCallPart(toolUse.ID, toolUse.Name, JsonSerializer.SerializeToElement(toolUse.Input)));
            else if (block.TryPickThinking(out var thinking))
                parts.Add(new OpaquePart(EngineName, JsonSerializer.SerializeToElement(new { type = "thinking", thinking = thinking.Thinking, signature = thinking.Signature })));
            else if (block.TryPickRedactedThinking(out var redacted))
                parts.Add(new OpaquePart(EngineName, JsonSerializer.SerializeToElement(new { type = "redacted_thinking", data = redacted.Data })));
        }

        var usage = response.Usage is { } u
            ? new EngineUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens ?? 0, u.CacheCreationInputTokens ?? 0)
            : null;
        return new EngineTurn(parts, MapStopReason(response.StopReason?.Raw()), usage);
    }

    private static EngineStopReason MapStopReason(string? stopReason) =>
        stopReason switch
        {
            "end_turn" or "stop_sequence" => EngineStopReason.EndTurn,
            "tool_use" => EngineStopReason.ToolUse,
            "max_tokens" or "model_context_window_exceeded" => EngineStopReason.MaxTokens,
            "refusal" => EngineStopReason.Refusal,
            _ => EngineStopReason.Other,
        };

    private static Dictionary<string, JsonElement> ToDictionary(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
}
