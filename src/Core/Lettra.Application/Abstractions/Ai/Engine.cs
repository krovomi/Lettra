using System.Text.Json;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// Port vers un modèle de langage. L'orchestrateur garde la boucle d'outils : un moteur ne fait que proposer du texte
/// ou des appels d'outils, il n'exécute rien et ne voit que des données pseudonymisées.
/// </summary>
public interface IAiEngine
{
    string Name { get; }

    Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken);
}

/// <summary><c>StableInstructions</c> : Partie stable du prompt système (surcouche Lettra et SKILL.md), mise en cache. <c>Context</c> : Partie variable (company.json virtuel pseudonymisé, date, fonction demandée).</summary>
public sealed record EngineRequest(
    string StableInstructions,
    string Context,
    IReadOnlyList<EngineMessage> Messages,
    IReadOnlyList<EngineToolDefinition> Tools);

public sealed record EngineToolDefinition(string Name, string Description, JsonElement InputSchema);

public enum EngineRole
{
    User = 1,
    Assistant = 2,
}

public sealed record EngineMessage(EngineRole Role, IReadOnlyList<EnginePart> Parts)
{
    public static EngineMessage UserText(string text) => new(EngineRole.User, [new TextPart(text)]);
}

public abstract record EnginePart;

public sealed record TextPart(string Text) : EnginePart;

public sealed record ToolCallPart(string Id, string Name, JsonElement Input) : EnginePart;

public sealed record ToolResultPart(string ToolCallId, string Content, bool IsError) : EnginePart;

/// <summary>Bloc propre à un moteur (raisonnement signé de Claude), renvoyé tel quel au même moteur.</summary>
public sealed record OpaquePart(string Engine, JsonElement Payload) : EnginePart;

public enum EngineStopReason
{
    EndTurn = 1,
    ToolUse = 2,
    MaxTokens = 3,
    Refusal = 4,
    Other = 5,
}

public sealed record EngineUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens)
{
    public static EngineUsage? Add(EngineUsage? left, EngineUsage? right) =>
        left is null ? right
        : right is null ? left
        : new EngineUsage(
            left.InputTokens + right.InputTokens,
            left.OutputTokens + right.OutputTokens,
            left.CacheReadTokens + right.CacheReadTokens,
            left.CacheWriteTokens + right.CacheWriteTokens);
}

public sealed record EngineTurn(IReadOnlyList<EnginePart> Parts, EngineStopReason StopReason, EngineUsage? Usage = null)
{
    public IEnumerable<ToolCallPart> ToolCalls => Parts.OfType<ToolCallPart>();

    public string Text => string.Concat(Parts.OfType<TextPart>().Select(p => p.Text));
}
