using System.Text.Json;
using Lettra.Application.Abstractions.Identity;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Ai;

/// <summary>
/// Lettra dans l'assistant IA du poste (protocole MCP). Le serveur ne fait que lire : il propose les outils en lecture
/// seule que les droits de l'utilisateur permettent, pseudonymise ce qui sort, rétablit ce qui entre et journalise chaque
/// appel dans le journal IA du dossier (annexe F §10).
/// </summary>
public interface IMcpServer
{
    /// <summary>Traite un message JSON-RPC du client. <paramref name="sessionId"/> est l'en-tête <c>Mcp-Session-Id</c>.</summary>
    Task<McpReply> HandleAsync(string? sessionId, JsonElement message, McpCaller caller, CancellationToken cancellationToken);

    /// <summary>Ferme une session : sa table de correspondance des pseudonymes est oubliée.</summary>
    bool End(string sessionId, McpCaller caller);
}

/// <summary>Qui appelle : l'utilisateur, le dossier de l'en-tête, ses droits effectifs et les réglages IA du cabinet.</summary>
public sealed record McpCaller(OrganisationId Organisation, DossierId Dossier, Guid UserId, IReadOnlySet<Permission> Permissions, AiSettings Settings);

/// <summary>
/// La réponse HTTP : 200 avec le JSON-RPC, 202 sans corps pour une notification, 400 pour un message illisible, 404 pour
/// une session inconnue ou expirée (le client recommence par <c>initialize</c>).
/// </summary>
public sealed record McpReply(int Status, string? Json = null, string? SessionId = null)
{
    /// <summary>Le corps de la requête n'est pas du JSON : erreur « parse error » de JSON-RPC, sans session.</summary>
    public static McpReply Unreadable { get; } = new(400, """{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"JSON illisible."}}""");
}
