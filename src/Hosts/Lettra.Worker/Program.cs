using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Jobs de fond des modes 2, 3 et 5. Aujourd'hui : migration du catalogue, reprise des créations de dossiers
// interrompues, puis migration de toutes les bases de dossiers par vagues (canari d'abord), avec bail et reprise.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLettra(builder.Configuration);
builder.Services.AddScoped<JobDossierSource>();
builder.Services.AddScoped<IRequestedDossierSource>(sp => sp.GetRequiredService<JobDossierSource>());
builder.Services.AddOptions<DossierMigrationJobOptions>().Bind(builder.Configuration.GetSection(DossierMigrationJobOptions.SectionName));
builder.Services.AddHostedService<DossierMigrationJob>();

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
