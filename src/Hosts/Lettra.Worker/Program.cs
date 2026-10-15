using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

// Jobs de fond des modes 2, 3 et 5 : migration du catalogue, reprise des créations de dossiers interrompues, migration
// de toutes les bases de dossiers par vagues (canari d'abord, avec bail et reprise), passe d'escalade de la Supervision,
// et passe de nuit de la chaîne anti-fraude (clôtures et contrôle d'intégrité de chaque dossier).
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLettra(builder.Configuration);

// Le Worker agit sous son propre compte de service, jamais comme un utilisateur anonyme ou emprunté.
builder.Services.AddOptions<WorkerIdentityOptions>().Bind(builder.Configuration.GetSection(WorkerIdentityOptions.SectionName));
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser, WorkerCurrentUser>());
builder.Services.AddScoped<JobDossierSource>();
builder.Services.AddScoped<IRequestedDossierSource>(sp => sp.GetRequiredService<JobDossierSource>());
builder.Services.AddOptions<DossierMigrationJobOptions>().Bind(builder.Configuration.GetSection(DossierMigrationJobOptions.SectionName));
builder.Services.AddHostedService<DossierMigrationJob>();
builder.Services.AddOptions<EscalationJobOptions>().Bind(builder.Configuration.GetSection(EscalationJobOptions.SectionName));
builder.Services.AddHostedService<EscalationJob>();
builder.Services.AddOptions<IntegrityCheckJobOptions>().Bind(builder.Configuration.GetSection(IntegrityCheckJobOptions.SectionName));
builder.Services.AddHostedService<IntegrityCheckJob>();

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
