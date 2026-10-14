using Lettra.Api.Endpoints;
using Lettra.Api.Infrastructure;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Contracts.Serialization;
using Lettra.Supervision.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

DeploymentMode mode = builder.Configuration.GetDeploymentMode();
if (mode == DeploymentMode.LocalNetwork && !builder.Environment.IsDevelopment())
{
    // Phase 0 : identité locale à utilisateur unique. Les comptes locaux (mode 2) et Keycloak arrivent en phase 2.
    throw new InvalidOperationException("L'identité locale à utilisateur unique est réservée au développement en phase 0.");
}

builder.Services.AddLettra(builder.Configuration);
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

RouteGroupBuilder api = app.MapGroup("/api/v1").AddEndpointFilter<TenantEndpointFilter>();
api.MapCustomers();
api.MapSuppliers();
api.MapCatalog();
api.MapDirectory();

app.MapHealthChecks("/health");

// Webhook de l'Alertmanager de la cellule (annexe G §4) : exposé seulement si un jeton est configuré.
app.MapSupervisionWebhooks();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Point d'entrée, exposé aux tests d'intégration.</summary>
public partial class Program;
