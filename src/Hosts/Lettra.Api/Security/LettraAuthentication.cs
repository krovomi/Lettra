using System.Diagnostics;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Diagnostics;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>
/// Authentification de l'API (annexe F §2, ADR-0010) : jeton porteur Keycloak pour MAUI, la synchro et les
/// services ; cookie de session du BFF pour le navigateur, qui ne détient jamais de jeton.
/// </summary>
public static class LettraAuthentication
{
    public static IServiceCollection AddLettraApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<OidcOptions>()
            .Validate(o => Uri.TryCreate(o.Authority, UriKind.Absolute, out _), $"{OidcOptions.SectionName}:Authority est obligatoire (realm Keycloak).")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());
        services.TryAddSingleton(TimeProvider.System);

        // Sessions du BFF : cache distribué (mémoire par défaut, Redis de la cellule en production), chiffrées.
        services.AddDistributedMemoryCache();
        services.AddDataProtection().SetApplicationName("Lettra");
        services.AddSingleton<ITicketStore, DistributedTicketStore>();
        services.AddHttpClient<ITokenEndpointClient, KeycloakTokenClient>();
        services.AddSingleton<BffTokenRefresher>();

        services.AddAuthentication(LettraAuthSchemes.Selector)
            .AddPolicyScheme(LettraAuthSchemes.Selector, displayName: null, o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? LettraAuthSchemes.Bearer
                    : LettraAuthSchemes.Cookie)
            .AddJwtBearer(LettraAuthSchemes.Bearer, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters.NameClaimType = OidcClaims.PreferredUserName;
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            })
            .AddCookie(LettraAuthSchemes.Cookie, o =>
            {
                o.Cookie.Name = "__Host-lettra";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = true;

                // L'API ne redirige jamais : la connexion passe par /bff/login.
                o.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                o.Events.OnValidatePrincipal = context =>
                    context.HttpContext.RequestServices.GetRequiredService<BffTokenRefresher>().ValidateAsync(context);
            })
            .AddOpenIdConnect(LettraAuthSchemes.Oidc, o =>
            {
                o.SignInScheme = LettraAuthSchemes.Cookie;
                o.ResponseType = "code";
                o.ResponseMode = "query";
                o.UsePkce = true;
                o.SaveTokens = true;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.CallbackPath = "/bff/signin-oidc";
                o.SignedOutCallbackPath = "/bff/signout-callback-oidc";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.TokenValidationParameters.NameClaimType = OidcClaims.PreferredUserName;
                o.Events.OnTokenValidated = context =>
                {
                    OidcOptions current = context.HttpContext.RequestServices.GetRequiredService<IOptions<OidcOptions>>().Value;
                    if (current.RequireMfa && (context.Principal is null || !OidcClaims.HasStrongAuthentication(context.Principal, current)))
                    {
                        // Session Keycloak sans second facteur prouvé (cas de l'enrôlement TOTP, action requise que Keycloak
                        // ne compte pas dans amr) : une seule nouvelle authentification complète, puis refus.
                        if (context.Properties?.Items.ContainsKey(BffEndpoints.ReauthenticationKey) == true)
                        {
                            context.Fail("Second facteur absent.");
                        }
                        else
                        {
                            context.HandleResponse();
                            context.Response.Redirect(BffEndpoints.ReauthenticationPath(context.Properties?.RedirectUri));
                        }
                    }

                    return Task.CompletedTask;
                };
                o.Events.OnTicketReceived = context =>
                {
                    context.Properties!.Items[BffEndpoints.SignOutTokenKey] = BffEndpoints.NewSignOutToken();
                    CountSignIn(LettraTelemetry.Outcomes.Success);
                    return Task.CompletedTask;
                };
                o.Events.OnRemoteFailure = context =>
                {
                    // Retour de Keycloak refusé (second facteur absent, état invalide, erreur du fournisseur) : compté,
                    // sans rien de l'utilisateur.
                    CountSignIn(LettraTelemetry.Outcomes.Failure);
                    context.HandleResponse();
                    context.Response.Redirect("/session?erreur=connexion");
                    return Task.CompletedTask;
                };
            });

        // Réglages Keycloak lus au démarrage de l'hôte (et non à l'enregistrement) : la configuration peut encore changer d'ici là.
        services.AddOptions<JwtBearerOptions>(LettraAuthSchemes.Bearer).Configure<IOptions<OidcOptions>>((o, oidc) =>
        {
            o.Authority = oidc.Value.Authority;
            o.Audience = oidc.Value.Audience;
            o.RequireHttpsMetadata = oidc.Value.RequireHttpsMetadata;
        });
        services.AddOptions<OpenIdConnectOptions>(LettraAuthSchemes.Oidc).Configure<IOptions<OidcOptions>>((o, oidc) =>
        {
            o.Authority = oidc.Value.Authority;
            o.ClientId = oidc.Value.BffClientId;
            o.ClientSecret = oidc.Value.BffClientSecret;
            o.RequireHttpsMetadata = oidc.Value.RequireHttpsMetadata;
        });
        services.AddOptions<CookieAuthenticationOptions>(LettraAuthSchemes.Cookie)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);

        services.AddSingleton<IAuthorizationHandler, MfaRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, LettraSubjectRequirement>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(LettraPolicies.User, policy => policy
                .AddAuthenticationSchemes(LettraAuthSchemes.Selector)
                .RequireAuthenticatedUser()
                .AddRequirements(new LettraSubjectRequirement(), new MfaRequirement()));

        return services;
    }

    private static void CountSignIn(string outcome) =>
        LettraTelemetry.SignIns.Add(1, new TagList { { LettraTelemetry.Attributes.Outcome, outcome } });
}
