using Microsoft.Extensions.Hosting;

// Squelette phase 2 : l'hôte démarre sans job. Le job de migration des bases de dossiers
// (verrou, reprise, vagues) arrive avec le chantier « Persistance et tenancy ».
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
