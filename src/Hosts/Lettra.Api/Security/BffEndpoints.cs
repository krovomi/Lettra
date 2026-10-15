using System.Security.Cryptography;
using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Identity;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Lettra.Api.Security;

/// <summary>
/// Backend for frontend du navigateur (annexe F §2) : connexion OIDC côté serveur, session par cookie
/// <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Strict</c>. Aucun jeton ne descend dans le navigateur.
/// </summary>
public static class BffEndpoints
{
    internal const string SignOutTokenKey = "lettra.signout";
    internal const string ReauthenticationKey = "lettra.reauthentification";

    public static IEndpointRouteBuilder MapLettraBff(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder bff = app.MapGroup("/bff").ExcludeFromDescription();

        // Navigation pleine page : Keycloak (SSO du cabinet, MFA), puis retour sur une adresse locale seulement.
        // Nouvelle authentification : prompt=login, Keycloak redemande mot de passe et second facteur.
        bff.MapGet("/login", (string? returnUrl, bool? reauthentification) =>
        {
            AuthenticationProperties properties = new() { RedirectUri = LocalOnly(returnUrl) };
            if (reauthentification == true)
            {
                properties.Items[ReauthenticationKey] = "1";
                properties.SetParameter(OpenIdConnectParameterNames.Prompt, "login");
            }

            return Results.Challenge(properties, [LettraAuthSchemes.Oidc]);
        });

        bff.MapGet("/user", async (HttpContext context) =>
        {
            AuthenticateResult session = await context.AuthenticateAsync(LettraAuthSchemes.Cookie).ConfigureAwait(false);
            if (!session.Succeeded || session.Principal is null)
            {
                return new[] { SecurityErrors.Unauthenticated }.ToProblem();
            }

            string? token = SignOutToken(session);
            return TypedResults.Ok(new SessionResponse(
                IsAuthenticated: true,
                SessionModeDto.Oidc,
                OidcClaims.DisplayName(session.Principal),
                LocalAccountExists: false,
                SignOutUrl: token is null ? null : $"/bff/logout?jeton={Uri.EscapeDataString(token)}",
                DeviceStorageKey: OidcClaims.UserId(session.Principal) is Guid user ? DeviceStorageKeyOf(user) : null));
        });

        // Déconnexion par navigation, protégée contre le CSRF par un jeton propre à la session (donné par /bff/user).
        bff.MapGet("/logout", async (HttpContext context, string? jeton) =>
        {
            AuthenticateResult session = await context.AuthenticateAsync(LettraAuthSchemes.Cookie).ConfigureAwait(false);
            if (!session.Succeeded)
            {
                return Results.LocalRedirect("/");
            }

            string? expected = SignOutToken(session);
            if (expected is null || jeton is null || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(jeton)))
            {
                return new[] { SecurityErrors.Forbidden }.ToProblem();
            }

            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [LettraAuthSchemes.Cookie, LettraAuthSchemes.Oidc]);
        });

        return app;
    }

    private static string? SignOutToken(AuthenticateResult session) =>
        session.Properties?.Items.TryGetValue(SignOutTokenKey, out string? token) == true ? token : null;

    /// <summary>
    /// Clé des brouillons hors ligne de l'utilisateur dans le navigateur (étape 4.5) : une empreinte de son identifiant,
    /// stable d'une session à l'autre, qui ne le nomme pas.
    /// </summary>
    internal static string DeviceStorageKeyOf(Guid userId) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"lettra.brouillons:{userId:N}")))[..32];

    internal static string NewSignOutToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    internal static string ReauthenticationPath(string? returnUrl) =>
        $"/bff/login?returnUrl={Uri.EscapeDataString(LocalOnly(returnUrl))}&reauthentification=true";

    /// <summary>Seulement un chemin local (<c>/…</c>, ni <c>//</c> ni <c>/\</c>) : pas de redirection ouverte.</summary>
    internal static string LocalOnly(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
            ? returnUrl
            : "/";
}
