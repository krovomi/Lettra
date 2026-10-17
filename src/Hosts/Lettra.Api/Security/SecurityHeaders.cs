using Microsoft.Net.Http.Headers;

namespace Lettra.Api.Security;

/// <summary>
/// En-têtes de sécurité de toutes les réponses (application Blazor, API, BFF) : politique de contenu (CSP), refus
/// de l'affichage dans un cadre, isolation d'origine, pas de reniflage de type ; et aucune mise en cache des
/// réponses de l'API et du BFF, qui portent des données de dossier ou de session.
/// <para>
/// CSP de Blazor WebAssembly : <c>'wasm-unsafe-eval'</c> autorise la compilation du runtime .NET (WebAssembly),
/// sans ouvrir <c>eval</c> au JavaScript ; aucun script en ligne (l'inscription du service worker est dans
/// <c>pwa.js</c>). Les attributs <c>style</c> posés par les composants (variables CSS de la grille de saisie) exigent
/// <c>style-src-attr 'unsafe-inline'</c> ; les feuilles de style restent limitées à l'origine.
/// </para>
/// <para>
/// HSTS (<see cref="StrictTransportSecurity"/>) : hors développement, sur les seules réponses servies en HTTPS (le
/// schéma transmis par le proxy compte, voir <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>) et jamais pour
/// <c>localhost</c> : la démonstration en <c>http://localhost:8080</c> n'en reçoit pas ; le profil HTTPS du réseau
/// local le reçoit, mais un navigateur l'ignore pour une adresse IP.
/// </para>
/// </summary>
public static class SecurityHeaders
{
    /// <summary>Politique de contenu servie partout, sauf en développement (voir <see cref="DevelopmentContentSecurityPolicy"/>).</summary>
    public static readonly string ContentSecurityPolicy = Csp("'self'");

    /// <summary>En développement : rechargement à chaud et débogage WASM ouvrent un WebSocket sur un autre port local.</summary>
    public static readonly string DevelopmentContentSecurityPolicy = Csp("'self' ws://localhost:* wss://localhost:* http://localhost:*");

    /// <summary>Le micro n'est ouvert qu'à l'application elle-même, pour la dictée (vague 5) ; tout le reste est fermé.</summary>
    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(self), payment=(), usb=()";

    /// <summary>Un an, sous-domaines compris ; servi en HTTPS hors développement et hors <c>localhost</c>.</summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    private static string Csp(string connect) =>
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self'; " +
        "style-src-attr 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        $"connect-src {connect}; " +
        "worker-src 'self'; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public static IApplicationBuilder UseLettraSecurityHeaders(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string csp = environment.IsDevelopment() ? DevelopmentContentSecurityPolicy : ContentSecurityPolicy;
        bool hsts = !environment.IsDevelopment();
        return app.Use((context, next) =>
        {
            // Au départ de la réponse : couvre aussi les fichiers statiques, les erreurs et les 401/429.
            context.Response.OnStarting(() =>
            {
                Apply(context, csp, hsts);
                return Task.CompletedTask;
            });
            return next(context);
        });
    }

    private static void Apply(HttpContext context, string csp, bool hsts)
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers[HeaderNames.ContentSecurityPolicy] = csp;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.XFrameOptions] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Cross-Origin-Embedder-Policy"] = "require-corp";
        if (hsts && context.Request.IsHttps && !IsLoopback(context.Request.Host.Host))
        {
            headers[HeaderNames.StrictTransportSecurity] = StrictTransportSecurity;
        }

        PathString path = context.Request.Path;
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase))
        {
            headers[HeaderNames.CacheControl] = "no-store";
            headers[HeaderNames.Pragma] = "no-cache";
        }
    }

    // Comme HstsOptions.ExcludedHosts : un HSTS sur localhost s'imposerait à tous les services locaux du poste.
    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]" or "::1";
}
