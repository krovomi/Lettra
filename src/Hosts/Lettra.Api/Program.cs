using Lettra.Api.Endpoints;
using Lettra.Api.Infrastructure;
using Lettra.Api.Presence;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Contracts.Serialization;
using Lettra.DemoData;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Outbox;
using Lettra.Supervision.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

DeploymentMode mode = builder.Configuration.GetDeploymentMode();
if (mode == DeploymentMode.IsolatedDesktop)
{
    // Le poste isolé n'expose pas d'API : il passe par la gateway in-process et le compte local.
    throw new InvalidOperationException("L'API sert les modes 2 à 5 ; le mode 1 est l'application MAUI seule.");
}

// Pas d'en-tête Server : la pile technique n'a pas à être annoncée.
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AddLettra(builder.Configuration);
builder.Services.AddLettraApiAuthentication(builder.Configuration);
builder.Services.AddLettraRateLimiting(builder.Configuration);
builder.Services.AddLettraPresence();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestedDossierSource, HeaderDossierSource>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default));
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Installation sans Worker (démonstration navigateur, petit réseau local) : l'API reprend elle-même les envois à la
// plateforme agréée mis en file pendant une panne. Ailleurs, c'est le Worker qui s'en charge.
if (bool.TryParse(builder.Configuration[OutboxApiHosting.Key], out bool outboxInApi) && outboxInApi)
{
    builder.Services.AddOutboxDispatch(builder.Configuration, (services, token) => services.ListActiveDossiersAsync(token));
}

WebApplication app = builder.Build();

// Catalogue : migration (sauf en SaaS, où le Worker migre) et import des déclarations de la configuration.
await app.Services.InitializeLettraAsync(app.Configuration, CancellationToken.None).ConfigureAwait(false);

// Démonstration navigateur : jeu « Cabinet Démo » dans le dossier ouvert, au premier démarrage seulement. Sans
// Lettra:Demo:Seed=true l'appel ne fait rien ; en environnement Production il refuse, quelle que soit la configuration.
await LettraDemoData.SeedAsync(app.Configuration, app.Environment.EnvironmentName, loggerFactory: app.Services.GetRequiredService<ILoggerFactory>()).ConfigureAwait(false);

// En-têtes de sécurité (CSP, cadres, isolation d'origine) et pas de cache pour l'API et le BFF : avant tout le reste.
app.UseLettraSecurityHeaders(app.Environment);

// Aucune exception ni pile d'appel ne sort : un code générique, le détail reste dans la trace technique.
app.UseExceptionHandler(errorApp => errorApp.Run(context => ErrorResults.Unexpected().ExecuteAsync(context)));
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Authentifié (MFA prouvée), puis anti-CSRF pour le cookie du BFF, puis dossier ; la permission vient par route.
RouteGroupBuilder api = app.MapGroup("/api/v1")
    .RequireAuthorization(LettraPolicies.User)
    .AddEndpointFilter<CsrfEndpointFilter>()
    .AddEndpointFilter<TenantEndpointFilter>();
api.MapCustomers();
api.MapSuppliers();
api.MapCatalog();
api.MapBilling();
api.MapDirectory();
api.MapAssistant();
api.MapSpeech();
api.MapIntegrity();
api.MapExports();
api.MapDossierAlerts();
api.MapSync();

// Administration du cabinet : même authentification et anti-CSRF, sans dossier ; la permission porte sur le cabinet.
RouteGroupBuilder cabinet = app.MapGroup($"/api/v1/cabinets/{{{CabinetPermissionEndpointFilter.RouteKey}:guid}}")
    .RequireAuthorization(LettraPolicies.User)
    .AddEndpointFilter<CsrfEndpointFilter>();
cabinet.MapDossiers();
cabinet.MapCabinetRights();
cabinet.MapCabinetAlerts();
cabinet.MapAssistantSettings();
cabinet.MapCabinetPolicies();

// Membres et dossiers d'un groupe : la permission porte sur le groupe de la route.
app.MapGroup($"/api/v1/groupes/{{{GroupPermissionEndpointFilter.RouteKey}:guid}}")
    .RequireAuthorization(LettraPolicies.User)
    .AddEndpointFilter<CsrfEndpointFilter>()
    .MapGroupRights();

// Les dossiers que l'utilisateur peut ouvrir (sélecteur de dossier) : pas encore de dossier choisi.
app.MapGroup("/api/v1/dossiers")
    .RequireAuthorization(LettraPolicies.User)
    .AddEndpointFilter<CsrfEndpointFilter>()
    .MapAccessibleDossiers();

// Instantané de droits signé pour le hors ligne (annexe F §4) et ses clés publiques.
app.MapRightsSnapshot();

// Serveur commun : ce que l'application installée lit pour se connecter (realm Keycloak public), sans connexion.
app.MapServerDiscovery();

app.MapLettraPresence();
app.MapLettraBff();
app.MapHealthChecks("/health");

// Webhook de l'Alertmanager de la cellule (annexe G §4) : exposé seulement si un jeton est configuré.
app.MapSupervisionWebhooks();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Point d'entrée, exposé aux tests d'intégration.</summary>
public partial class Program;

/// <summary>Réglage qui confie la passe de l'outbox à l'API, pour une installation sans Worker.</summary>
internal static class OutboxApiHosting
{
    public const string Key = "Lettra:Outbox:Job:HostedByApi";
}
