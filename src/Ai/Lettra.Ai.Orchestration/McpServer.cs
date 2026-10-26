using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Ai.Privacy;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Les sessions de l'assistant IA du poste : une par conversation, liée à l'utilisateur et au dossier qui l'ont ouverte,
/// avec sa table de correspondance des pseudonymes. Rien n'est écrit sur disque ; une session inactive depuis
/// <see cref="IdleTimeout"/> est oubliée.
/// </summary>
internal sealed class McpSessions(TimeProvider time)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, McpSession> _sessions = new(StringComparer.Ordinal);

    public McpSession Open(McpCaller caller, PseudonymizationSession privacy)
    {
        Purge();
        McpSession session = new(Guid.NewGuid().ToString("N"), caller.Dossier, caller.UserId, privacy, time.GetUtcNow());
        _sessions[session.Id] = session;
        return session;
    }

    /// <summary>La session, si elle existe encore et appartient à cet utilisateur dans ce dossier.</summary>
    public McpSession? Find(string? id, McpCaller caller)
    {
        Purge();
        if (id is null || !_sessions.TryGetValue(id, out McpSession? session) || session.Dossier != caller.Dossier || session.UserId != caller.UserId)
        {
            return null;
        }

        session.LastUsed = time.GetUtcNow();
        return session;
    }

    public bool End(string id, McpCaller caller) => Find(id, caller) is not null && _sessions.TryRemove(id, out _);

    private void Purge()
    {
        DateTimeOffset limit = time.GetUtcNow() - IdleTimeout;
        foreach (KeyValuePair<string, McpSession> stale in _sessions.Where(s => s.Value.LastUsed < limit))
        {
            _sessions.TryRemove(stale.Key, out _);
        }
    }
}

internal sealed class McpSession(string id, DossierId dossier, Guid userId, PseudonymizationSession privacy, DateTimeOffset lastUsed)
{
    public string Id { get; } = id;

    public DossierId Dossier { get; } = dossier;

    public Guid UserId { get; } = userId;

    public PseudonymizationSession Privacy { get; } = privacy;

    public DateTimeOffset LastUsed { get; set; } = lastUsed;
}

/// <summary>
/// Le serveur MCP de Lettra (transport HTTP, réponses JSON). Il ne connaît que <c>initialize</c>, <c>ping</c>,
/// <c>tools/list</c> et <c>tools/call</c> : les outils proposés sont ceux du point
/// <see cref="AssistantFeature.ExternalAssistant"/>, en lecture seule, que les droits de l'utilisateur permettent. Ce qui
/// sort est pseudonymisé ; chaque connexion, appel et refus va au journal IA du dossier, pseudonymisé lui aussi.
/// </summary>
internal sealed class McpServer(
    McpSessions sessions,
    ToolRegistry tools,
    IAiDossierContext dossiers,
    IAiAuditLog audit,
    TimeProvider time,
    ILogger<McpServer> logger) : IMcpServer
{
    public const string EngineName = "mcp";

    public static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;
    private const int MaxClientNameLength = 80;

    private static readonly JsonElement EmptyArguments = JsonSerializer.SerializeToElement(new JsonObject(), AiToolResult.JsonOptions);

    public async Task<McpReply> HandleAsync(string? sessionId, JsonElement message, McpCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("jsonrpc", out JsonElement version) || version.GetString() != "2.0")
        {
            return new McpReply(400, Error(null, InvalidRequest, "Message JSON-RPC 2.0 attendu (un seul objet)."));
        }

        JsonNode? id = message.TryGetProperty("id", out JsonElement idElement) ? JsonNode.Parse(idElement.GetRawText()) : null;
        string? method = message.TryGetProperty("method", out JsonElement methodElement) ? methodElement.GetString() : null;
        if (method is null || id is null)
        {
            // Une notification ou une réponse du client : rien à répondre.
            return new McpReply(202);
        }

        JsonElement parameters = message.TryGetProperty("params", out JsonElement p) ? p : default;
        if (method == "initialize")
        {
            return await InitializeAsync(id, parameters, caller, cancellationToken).ConfigureAwait(false);
        }

        if (sessions.Find(sessionId, caller) is not { } session)
        {
            return new McpReply(404, Error(id, InvalidRequest, "Session inconnue ou expirée : recommencez par initialize."));
        }

        return method switch
        {
            "ping" => Ok(id, new JsonObject(), session),
            "tools/list" => Ok(id, new JsonObject { ["tools"] = new JsonArray([.. Available(caller).Select(Describe)]) }, session),
            "tools/call" => await CallAsync(id, parameters, caller, session, cancellationToken).ConfigureAwait(false),
            _ => new McpReply(200, Error(id, MethodNotFound, $"Méthode inconnue : {method}."), session.Id),
        };
    }

    public bool End(string sessionId, McpCaller caller) => sessions.End(sessionId, caller);

    private async Task<McpReply> InitializeAsync(JsonNode id, JsonElement parameters, McpCaller caller, CancellationToken cancellationToken)
    {
        PseudonymizationSession privacy = new(await dossiers.GetKnownIdentitiesAsync(caller.Dossier, cancellationToken).ConfigureAwait(false));
        McpSession session = sessions.Open(caller, privacy);
        string requested = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out JsonElement v) ? v.GetString() ?? string.Empty : string.Empty;
        string client = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("clientInfo", out JsonElement info) && info.TryGetProperty("name", out JsonElement name)
            ? name.GetString() ?? "?"
            : "?";
        await AuditAsync(caller, AiAuditKind.Request, $"assistant IA du poste connecté (MCP) : {privacy.Pseudonymize(client[..Math.Min(client.Length, MaxClientNameLength)])}", cancellationToken).ConfigureAwait(false);

        JsonObject result = new()
        {
            ["protocolVersion"] = ProtocolVersions.Contains(requested, StringComparer.Ordinal) ? requested : ProtocolVersions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "lettra", ["title"] = "Lettra", ["version"] = "1.0" },
            ["instructions"] =
                "Lettra, logiciel de facturation d'un cabinet d'expertise comptable, en lecture seule. Les outils calculent en C# déterministe " +
                "avec les droits de l'utilisateur ; citez leurs chiffres, ne les recalculez pas. Les noms des clients, fournisseurs et personnes " +
                "sont remplacés par des jetons comme [CLIENT_1] : gardez-les tels quels. Rien ne peut être créé, validé, envoyé ni supprimé.",
        };
        return Ok(id, result, session);
    }

    private async Task<McpReply> CallAsync(JsonNode id, JsonElement parameters, McpCaller caller, McpSession session, CancellationToken cancellationToken)
    {
        string? name = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
        JsonElement input = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out JsonElement a) && a.ValueKind == JsonValueKind.Object
            ? a
            : EmptyArguments;
        IAiTool? tool = Available(caller).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (tool is null)
        {
            await AuditAsync(caller, AiAuditKind.ToolRejected, $"outil inconnu ou non autorisé : {session.Privacy.Pseudonymize(name ?? "?")}", cancellationToken).ConfigureAwait(false);
            return new McpReply(200, Error(id, InvalidParams, $"Outil inconnu ou non autorisé : {name}."), session.Id);
        }

        // Le journal garde l'entrée pseudonymisée : l'assistant du poste peut y avoir écrit un vrai nom.
        string logged = session.Privacy.Pseudonymize(input.GetRawText());
        if (ToolInvocation.Reject(tool, input) is { } invalid)
        {
            await AuditAsync(caller, AiAuditKind.ToolRejected, $"{tool.Name} : entrée invalide ({invalid})", cancellationToken).ConfigureAwait(false);
            return Ok(id, Content($"Paramètres invalides : {invalid}", isError: true), session);
        }

        AiToolContext context = new(caller.Organisation, caller.Dossier, caller.UserId, caller.Settings, OpenDocumentId: null);
        AiToolResult result = await ToolInvocation.RunAsync(tool, input, context, session.Privacy, logger, cancellationToken).ConfigureAwait(false);
        await AuditAsync(caller, AiAuditKind.ToolCall, $"{tool.Name} {logged} → {(result.IsError ? "erreur" : "ok")}", cancellationToken).ConfigureAwait(false);
        return Ok(id, Content(Pseudonymize(session.Privacy, result.Content), result.IsError), session);
    }

    /// <summary>Les outils de l'assistant du poste que l'utilisateur peut appeler, en lecture seule seulement.</summary>
    private IEnumerable<IAiTool> Available(McpCaller caller) =>
        tools.For(AssistantFeature.ExternalAssistant, caller.Permissions, caller.Settings).Where(t => t.Risk == AssistantToolRisk.ReadOnly);

    private static JsonObject Describe(IAiTool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
        ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["openWorldHint"] = false },
    };

    private static string Pseudonymize(PseudonymizationSession privacy, string content)
    {
        try
        {
            return privacy.PseudonymizeJson(content);
        }
        catch (JsonException)
        {
            return privacy.Pseudonymize(content);
        }
    }

    private static JsonObject Content(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static McpReply Ok(JsonNode id, JsonNode result, McpSession session) =>
        new(200, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString(AiToolResult.JsonOptions), session.Id);

    private static string Error(JsonNode? id, int code, string text) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = text } }
            .ToJsonString(AiToolResult.JsonOptions);

    private ValueTask AuditAsync(McpCaller caller, AiAuditKind kind, string detail, CancellationToken cancellationToken) =>
        audit.AppendAsync(new AiAuditEntry(time.GetUtcNow(), caller.Dossier, caller.UserId, kind, detail, EngineName), cancellationToken);
}
