using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lettra.Contracts.Common;

namespace Lettra.UI.Gateway.Http.Device;

/// <summary>
/// Client OIDC public de l'application de bureau : code d'autorisation + PKCE (S256), <c>state</c> et <c>nonce</c>,
/// rafraîchissement à usage unique tolérant à une réponse perdue (ADR-0010, constat 1 du POC synchro).
/// <para>
/// Le jeton d'identité est reçu directement du point de jeton de Keycloak en TLS : l'émetteur, l'audience, le nonce
/// et l'échéance sont vérifiés, la signature ne l'est pas (OIDC Core §3.1.3.7, point 6). Le jeton d'accès n'est jamais
/// lu par le poste : l'API le vérifie.
/// </para>
/// </summary>
public sealed class OidcPkceClient(HttpClient http, DeviceSignInOptions options, ITokenVault vault, IAuthorizationBrowser browser, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Endpoints? _endpoints;

    /// <summary>Ouvre la page de connexion du cabinet dans le navigateur système et garde les jetons obtenus.</summary>
    public async Task<OperationResult<DeviceTokens>> SignInAsync(CancellationToken cancellationToken)
    {
        Endpoints endpoints;
        try
        {
            endpoints = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return OperationResult<DeviceTokens>.Failure(ErrorCodes.Api.Unreachable);
        }

        string verifier = RandomToken();
        string state = RandomToken();
        string nonce = RandomToken();
        // Adresse telle qu'enregistrée dans le realm : Uri.ToString() ajouterait « / » à fr.lettra.app://callback, refusé
        // par Keycloak (« Invalid parameter: redirect_uri »).
        Uri redirect = browser.PrepareRedirectUri();
        Uri authorize = new(endpoints.Authorization + "?" + Form(
            ("response_type", "code"),
            ("client_id", options.ClientId),
            ("redirect_uri", redirect.OriginalString),
            ("scope", options.Scope),
            ("state", state),
            ("nonce", nonce),
            ("code_challenge", Challenge(verifier)),
            ("code_challenge_method", "S256")));

        Uri? callback = await browser.AuthorizeAsync(authorize, redirect, cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> query = callback is null ? [] : ParseQuery(callback.Query);
        if (!query.TryGetValue("state", out string? returnedState)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(returnedState), Encoding.ASCII.GetBytes(state))
            || !query.TryGetValue("code", out string? code))
        {
            return OperationResult<DeviceTokens>.Failure(ErrorCodes.Security.Unauthenticated);
        }

        TokenResult result = await RequestTokensAsync(endpoints, cancellationToken,
            ("grant_type", "authorization_code"),
            ("code", code),
            ("redirect_uri", redirect.OriginalString),
            ("client_id", options.ClientId),
            ("code_verifier", verifier)).ConfigureAwait(false);
        if (result.Tokens is null)
        {
            return OperationResult<DeviceTokens>.Failure(result.Unreachable ? ErrorCodes.Api.Unreachable : ErrorCodes.Security.Unauthenticated);
        }

        DeviceTokens? tokens = WithIdentity(result.Tokens, result.IdToken, endpoints.Issuer, nonce);
        if (tokens is null)
        {
            return OperationResult<DeviceTokens>.Failure(ErrorCodes.Security.Unauthenticated);
        }

        await vault.WriteAsync(tokens, cancellationToken).ConfigureAwait(false);
        return OperationResult<DeviceTokens>.Success(tokens);
    }

    /// <summary>
    /// Jeton d'accès valable, renouvelé si besoin (un seul rafraîchissement à la fois). <c>null</c> : il faut se reconnecter,
    /// ou le serveur est injoignable et le jeton a échu.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        DeviceTokens? current = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || IsFresh(current))
        {
            return current?.AccessToken;
        }

        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Un autre appel a peut-être déjà renouvelé pendant l'attente.
            current = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || IsFresh(current))
            {
                return current?.AccessToken;
            }

            if (current.RefreshToken is null)
            {
                return StillValid(current);
            }

            Endpoints endpoints;
            try
            {
                endpoints = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return StillValid(current);
            }

            TokenResult result = await RequestTokensAsync(endpoints, cancellationToken,
                ("grant_type", "refresh_token"),
                ("refresh_token", current.RefreshToken),
                ("client_id", options.ClientId)).ConfigureAwait(false);

            if (result.Rejected)
            {
                // Jeton révoqué, réutilisé ou échu : la session est finie, il faut se reconnecter.
                await vault.ClearAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            if (result.Tokens is null)
            {
                // Réponse perdue ou serveur injoignable : on garde l'ancien jeton de rafraîchissement, que Keycloak
                // accepte encore une fois tant que le nouveau n'a pas servi (refreshTokenMaxReuse = 1).
                return StillValid(current);
            }

            DeviceTokens renewed = result.Tokens with
            {
                RefreshToken = result.Tokens.RefreshToken ?? current.RefreshToken,
                IdToken = result.IdToken ?? current.IdToken,
                UserId = current.UserId,
                DisplayName = current.DisplayName,
            };
            await vault.WriteAsync(renewed, cancellationToken).ConfigureAwait(false);
            return renewed.AccessToken;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Révoque le jeton de rafraîchissement auprès de Keycloak (au mieux) et vide le coffre.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        DeviceTokens? current = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        await vault.ClearAsync(cancellationToken).ConfigureAwait(false);
        if (current?.RefreshToken is null)
        {
            return;
        }

        try
        {
            Endpoints endpoints = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
            if (endpoints.Revocation is not null)
            {
                using FormUrlEncodedContent content = new(
                [
                    new("token", current.RefreshToken),
                    new("token_type_hint", "refresh_token"),
                    new("client_id", options.ClientId),
                ]);
                using HttpResponseMessage response = await http.PostAsync(endpoints.Revocation, content, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Hors ligne : le jeton expirera de lui-même ; le poste n'en garde aucune trace.
        }
    }

    public void Dispose() => _refresh.Dispose();

    private bool IsFresh(DeviceTokens tokens) => tokens.AccessTokenExpiresAt - options.RefreshMargin > clock.GetUtcNow();

    private string? StillValid(DeviceTokens tokens) => tokens.AccessTokenExpiresAt > clock.GetUtcNow() ? tokens.AccessToken : null;

    private async Task<Endpoints> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (_endpoints is not null)
        {
            return _endpoints;
        }

        Uri authority = options.Authority ?? throw new InvalidOperationException($"{DeviceSignInOptions.SectionName}:Authority est obligatoire.");
        Uri discovery = new(authority.ToString().TrimEnd('/') + "/.well-known/openid-configuration");
        using HttpResponseMessage response = await http.GetAsync(discovery, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        JsonElement root = document.RootElement;
        string issuer = root.GetProperty("issuer").GetString()!;
        if (!string.Equals(issuer.TrimEnd('/'), authority.ToString().TrimEnd('/'), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Émetteur OIDC inattendu.");
        }

        _endpoints = new Endpoints(
            issuer,
            RequireHttps(root.GetProperty("authorization_endpoint").GetString()!, authority),
            RequireHttps(root.GetProperty("token_endpoint").GetString()!, authority),
            root.TryGetProperty("revocation_endpoint", out JsonElement revocation) ? RequireHttps(revocation.GetString()!, authority) : null);
        return _endpoints;
    }

    /// <summary>Les points d'accès ont le schéma du realm (HTTPS hors développement local).</summary>
    private static Uri RequireHttps(string value, Uri authority)
    {
        Uri uri = new(value);
        return uri.Scheme == authority.Scheme ? uri : throw new InvalidOperationException("Point d'accès OIDC sur un autre schéma que le realm.");
    }

    private async Task<TokenResult> RequestTokensAsync(Endpoints endpoints, CancellationToken cancellationToken, params (string Key, string Value)[] form)
    {
        try
        {
            using FormUrlEncodedContent content = new(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
            using HttpResponseMessage response = await http.PostAsync(endpoints.Token, content, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                return new TokenResult(null, null, Rejected: true, Unreachable: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new TokenResult(null, null, Rejected: false, Unreachable: true);
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            JsonElement root = document.RootElement;
            int expiresIn = root.TryGetProperty("expires_in", out JsonElement e) ? e.GetInt32() : 300;
            DeviceTokens tokens = new(
                root.GetProperty("access_token").GetString()!,
                clock.GetUtcNow().AddSeconds(expiresIn),
                root.TryGetProperty("refresh_token", out JsonElement refresh) ? refresh.GetString() : null,
                null,
                Guid.Empty,
                null);
            string? idToken = root.TryGetProperty("id_token", out JsonElement id) ? id.GetString() : null;
            return new TokenResult(tokens, idToken, Rejected: false, Unreachable: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new TokenResult(null, null, Rejected: false, Unreachable: true);
        }
    }

    /// <summary>Lit le jeton d'identité ; <c>null</c> si l'émetteur, l'audience, le nonce ou l'échéance ne conviennent pas.</summary>
    private DeviceTokens? WithIdentity(DeviceTokens tokens, string? idToken, string issuer, string nonce)
    {
        string[] parts = idToken?.Split('.') ?? [];
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            JsonElement claims = document.RootElement;
            JsonElement aud = claims.GetProperty("aud");
            bool audience = aud.ValueKind == JsonValueKind.Array
                ? aud.EnumerateArray().Any(a => a.GetString() == options.ClientId)
                : aud.GetString() == options.ClientId;
            if (claims.GetProperty("iss").GetString() != issuer
                || !audience
                || claims.GetProperty("nonce").GetString() != nonce
                || DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64()) <= clock.GetUtcNow()
                || !Guid.TryParse(claims.GetProperty("sub").GetString(), out Guid user))
            {
                return null;
            }

            string? name = claims.TryGetProperty("name", out JsonElement n) ? n.GetString()
                : claims.TryGetProperty("preferred_username", out JsonElement u) ? u.GetString() : null;
            return tokens with { IdToken = idToken, UserId = user, DisplayName = name };
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string Challenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string RandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string Form(params (string Key, string Value)[] values) =>
        string.Join('&', values.Select(v => $"{Uri.EscapeDataString(v.Key)}={Uri.EscapeDataString(v.Value)}"));

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            values.TryAdd(key, value);
        }

        return values;
    }

    private sealed record Endpoints(string Issuer, Uri Authorization, Uri Token, Uri? Revocation);

    private sealed record TokenResult(DeviceTokens? Tokens, string? IdToken, bool Rejected, bool Unreachable);
}
