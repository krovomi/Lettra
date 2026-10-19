using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Serveur commun : l'application installée sur un ordinateur, une tablette ou un téléphone ne demande que l'adresse du
/// serveur ; elle lit ici où se connecter (le realm Keycloak du cabinet) puis travaille sur la même base que le
/// navigateur, par l'API. Publié sans connexion : rien de secret.
/// </summary>
internal static class ServerDiscoveryEndpoints
{
    /// <summary>Realm tel que les appareils le voient, quand l'émetteur publié par Keycloak ne convient pas (mandataire).</summary>
    public const string PublicAuthorityKey = $"{OidcOptions.SectionName}:PublicAuthority";

    /// <summary>Client public des applications installées (code + PKCE), <c>lettra-maui</c> par défaut.</summary>
    public const string DeviceClientIdKey = $"{OidcOptions.SectionName}:DeviceClientId";

    public static IEndpointRouteBuilder MapServerDiscovery(this IEndpointRouteBuilder app)
    {
        app.MapGet("/" + ServerDiscoveryResponse.Path, DiscoverAsync)
            .AllowAnonymous()
            .WithTags("Identity")
            .WithName("ServerDiscovery")
            .Produces<ServerDiscoveryResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> DiscoverAsync(IConfiguration configuration, IOptionsMonitor<JwtBearerOptions> bearer, CancellationToken cancellationToken)
    {
        string clientId = configuration[DeviceClientIdKey] is { Length: > 0 } configured ? configured : "lettra-maui";
        string? authority = configuration[PublicAuthorityKey];
        if (string.IsNullOrWhiteSpace(authority))
        {
            // L'API joint Keycloak par son adresse interne (réseau Docker, service Kubernetes) ; l'émetteur publié dans la
            // découverte est l'adresse publique, celle que l'appareil ouvrira et que portent les jetons.
            try
            {
                JwtBearerOptions options = bearer.Get(LettraAuthSchemes.Bearer);
                authority = options.ConfigurationManager is { } manager
                    ? (await manager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false)).Issuer
                    : null;
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            {
                authority = null;
            }
        }

        return Uri.TryCreate(authority, UriKind.Absolute, out Uri? realm)
            ? TypedResults.Ok(new ServerDiscoveryResponse(ServerDiscoveryResponse.LettraProduct, realm.ToString().TrimEnd('/'), clientId))
            // Keycloak injoignable depuis l'API : l'appareil ne pourrait pas non plus s'y connecter.
            : TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: ErrorCodes.Api.Unreachable,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = ErrorCodes.Api.Unreachable });
    }
}
