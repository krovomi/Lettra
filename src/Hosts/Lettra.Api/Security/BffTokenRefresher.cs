using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>Réponse du point de jeton à un rafraîchissement.</summary>
internal sealed record TokenRefreshResult(TokenRefreshOutcome Outcome, string? AccessToken = null, string? RefreshToken = null, DateTimeOffset? ExpiresAt = null);

internal enum TokenRefreshOutcome
{
    Refreshed = 1,

    /// <summary>Jeton de rafraîchissement refusé (<c>invalid_grant</c>) : session révoquée ou réutilisation détectée.</summary>
    Revoked = 2,

    /// <summary>Keycloak injoignable ou erreur passagère : on réessaiera à la prochaine requête.</summary>
    Unavailable = 3,
}

/// <summary>Appel du point de jeton Keycloak (<c>grant_type=refresh_token</c>).</summary>
internal interface ITokenEndpointClient
{
    Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>
/// Garde la session du BFF en vie : le jeton d'accès (5 minutes) est rafraîchi peu avant son expiration. Un retrait
/// côté Keycloak (session révoquée, compte désactivé) ferme la session au rafraîchissement suivant.
/// <para>
/// Les requêtes simultanées d'un même navigateur partagent <b>un seul</b> rafraîchissement : avec la rotation des
/// jetons de rafraîchissement, deux appels parallèles avec le même jeton seraient pris pour une réutilisation et
/// révoqueraient la session. La tolérance d'une réutilisation (POC synchro, ADR-0010) couvre la réponse perdue ;
/// ce regroupement couvre la course.
/// </para>
/// </summary>
internal sealed partial class BffTokenRefresher(ITokenEndpointClient client, TimeProvider clock, ILogger<BffTokenRefresher> logger)
{
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShareResultFor = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, Lazy<Task<TokenRefreshResult>>> _inFlight = new(StringComparer.Ordinal);

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? refreshToken = context.Properties.GetTokenValue("refresh_token");
        string? expiresAt = context.Properties.GetTokenValue("expires_at");
        if (refreshToken is null || !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expires))
        {
            return;
        }

        if (expires - clock.GetUtcNow() > RefreshBefore)
        {
            return;
        }

        TokenRefreshResult result = await RefreshOnceAsync(refreshToken, context.HttpContext.RequestAborted).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case TokenRefreshOutcome.Refreshed:
                context.Properties.UpdateTokenValue("access_token", result.AccessToken!);
                context.Properties.UpdateTokenValue("refresh_token", result.RefreshToken ?? refreshToken);
                context.Properties.UpdateTokenValue("expires_at", result.ExpiresAt!.Value.ToString("o", CultureInfo.InvariantCulture));
                context.ShouldRenew = true;
                break;

            case TokenRefreshOutcome.Revoked:
                LogRevoked(logger);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(LettraAuthSchemes.Cookie).ConfigureAwait(false);
                break;

            default:
                // Keycloak indisponible : la session du BFF reste valide jusqu'à son propre délai d'inactivité.
                LogUnavailable(logger);
                break;
        }
    }

    private Task<TokenRefreshResult> RefreshOnceAsync(string refreshToken, CancellationToken cancellationToken)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        Lazy<Task<TokenRefreshResult>> refresh = _inFlight.GetOrAdd(key, _key => new Lazy<Task<TokenRefreshResult>>(async () =>
        {
            try
            {
                return await client.RefreshAsync(refreshToken, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                // Le résultat reste partagé un court instant, pour les requêtes qui portaient encore l'ancien ticket.
                _ = ForgetLaterAsync(key);
            }
        }));
        return refresh.Value.WaitAsync(cancellationToken);
    }

    private async Task ForgetLaterAsync(string key)
    {
        await Task.Delay(ShareResultFor, clock).ConfigureAwait(false);
        _inFlight.TryRemove(key, out _);
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "Session du BFF fermée : rafraîchissement refusé par le fournisseur d'identité")]
    private static partial void LogRevoked(ILogger logger);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Rafraîchissement du jeton impossible pour l'instant ; nouvel essai à la prochaine requête")]
    private static partial void LogUnavailable(ILogger logger);
}

/// <summary>Client du point de jeton Keycloak, découvert par le document OpenID du realm.</summary>
internal sealed class KeycloakTokenClient(HttpClient http, IOptionsMonitor<OpenIdConnectOptions> oidc, TimeProvider clock) : ITokenEndpointClient
{
    public async Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        OpenIdConnectOptions options = oidc.Get(LettraAuthSchemes.Oidc);
        try
        {
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            using FormUrlEncodedContent body = new(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret ?? string.Empty,
            });
            using HttpResponseMessage response = await http.PostAsync(new Uri(configuration.TokenEndpoint), body, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                return new TokenRefreshResult(TokenRefreshOutcome.Revoked);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new TokenRefreshResult(TokenRefreshOutcome.Unavailable);
            }

            using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = json.RootElement;
            return new TokenRefreshResult(
                TokenRefreshOutcome.Refreshed,
                root.GetProperty("access_token").GetString(),
                root.TryGetProperty("refresh_token", out JsonElement rotated) ? rotated.GetString() : null,
                clock.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or JsonException)
        {
            return new TokenRefreshResult(TokenRefreshOutcome.Unavailable);
        }
    }
}
