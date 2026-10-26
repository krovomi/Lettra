using System.Text.Json.Serialization;
using Lettra.Api.Endpoints;
using Lettra.Api.Infrastructure;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>
/// Connexion d'un assistant IA du poste au point MCP (<c>/api/v1/assistant/mcp</c>), comme le demande la
/// spécification MCP : métadonnées de la ressource protégée (RFC 9728) qui désignent Keycloak, adresse de ces
/// métadonnées dans le 401, et refus d'une page web d'un autre hôte. Le jeton s'obtient auprès du client public
/// Keycloak <c>lettra-mcp</c> (code + PKCE) ; l'API le vérifie comme tout jeton porteur (audience, second facteur).
/// </summary>
public static class McpClientConnection
{
    /// <summary>Le point MCP de l'API.</summary>
    public const string ResourcePath = "/api/v1/assistant/mcp";

    /// <summary>Les métadonnées de la ressource protégée, à la racine et suffixées du chemin de la ressource (RFC 9728 §3.1).</summary>
    public const string MetadataPath = "/.well-known/oauth-protected-resource";

    /// <summary>Le refus d'authentification sur le point MCP indique où lire les métadonnées ; ailleurs, <c>Bearer</c> seul.</summary>
    public static string WwwAuthenticate(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsMcp(request)
            ? $"Bearer resource_metadata=\"{Origin(request)}{MetadataPath}{ResourcePath}\""
            : "Bearer";
    }

    /// <summary>
    /// Refuse (403) une requête vers le point MCP envoyée par une page web d'un autre hôte : un navigateur détourné
    /// (rebinding DNS) ne peut pas s'en servir. Un client du poste n'envoie pas d'en-tête <c>Origin</c>.
    /// </summary>
    public static IApplicationBuilder UseMcpOriginCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            string? origin = context.Request.Headers.Origin;
            if (IsMcp(context.Request) && !string.IsNullOrEmpty(origin) && !SameHost(origin, context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

    /// <summary>
    /// Les métadonnées de la ressource protégée, sans connexion : la ressource, le realm Keycloak tel que le poste le
    /// voit (comme la découverte du serveur commun), le jeton en en-tête.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpProtectedResource(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        foreach (string path in new[] { MetadataPath, MetadataPath + ResourcePath })
        {
            app.MapGet(path, DescribeAsync).AllowAnonymous().ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<IResult> DescribeAsync(HttpRequest request, IConfiguration configuration, IOptionsMonitor<JwtBearerOptions> bearer, CancellationToken cancellationToken) =>
        await ServerDiscoveryEndpoints.PublicAuthorityAsync(configuration, bearer, cancellationToken).ConfigureAwait(false) is { } realm
            ? TypedResults.Json(
                new ProtectedResourceMetadata(Origin(request) + ResourcePath, [realm], ["header"], ["openid", "profile"], "Lettra"),
                McpJsonContext.Default.ProtectedResourceMetadata)
            : new[] { Error.Unavailable(ErrorCodes.Api.Unreachable) }.ToProblem();

    private static bool IsMcp(HttpRequest request) => request.Path.Equals(ResourcePath, StringComparison.OrdinalIgnoreCase);

    private static string Origin(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    // L'hôte seul : derrière un proxy TLS (démo sur le réseau local), l'API voit http là où la page est en https.
    private static bool SameHost(string origin, HttpRequest request) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Métadonnées de la ressource protégée (RFC 9728 §2).</summary>
public sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
    [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("resource_name")] string ResourceName);

[JsonSerializable(typeof(ProtectedResourceMetadata))]
internal sealed partial class McpJsonContext : JsonSerializerContext;
