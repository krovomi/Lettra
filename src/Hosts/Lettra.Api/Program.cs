using Lettra.Api.Endpoints;
using Lettra.Api.Infrastructure;
using Lettra.Api.Security;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Contracts.Serialization;
using Lettra.Supervision.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

DeploymentMode mode = builder.Configuration.GetDeploymentMode();
if (mode == DeploymentMode.IsolatedDesktop)
{
    // Le poste isolé n'expose pas d'API : il passe par la gateway in-process et le compte local.
    throw new InvalidOperationException("L'API sert les modes 2 à 5 ; le mode 1 est l'application MAUI seule.");
}

builder.Services.AddLettra(builder.Configuration);
builder.Services.AddLettraApiAuthentication(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestedDossierSource, HeaderDossierSource>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default));
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

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

// Authentifié (MFA prouvée), puis anti-CSRF pour le cookie du BFF, puis dossier ; la permission vient par route.
RouteGroupBuilder api = app.MapGroup("/api/v1")
    .RequireAuthorization(LettraPolicies.User)
    .AddEndpointFilter<CsrfEndpointFilter>()
    .AddEndpointFilter<TenantEndpointFilter>();
api.MapCustomers();
api.MapSuppliers();
api.MapCatalog();
api.MapDirectory();
api.MapAssistant();

app.MapLettraBff();
app.MapHealthChecks("/health");

// Webhook de l'Alertmanager de la cellule (annexe G §4) : exposé seulement si un jeton est configuré.
app.MapSupervisionWebhooks();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Point d'entrée, exposé aux tests d'intégration.</summary>
public partial class Program;
