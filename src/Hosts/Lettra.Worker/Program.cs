using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Bootstrap;
using Lettra.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Jobs de fond des modes 2, 3 et 5 : migration du catalogue, reprise des créations de dossiers interrompues, migration
// de toutes les bases de dossiers par vagues (canari d'abord, avec bail et reprise), passe d'escalade de la Supervision,
// passe quotidienne de la chaîne anti-fraude (clôtures et contrôle d'intégrité de chaque dossier), export FEC en masse et
// passe de l'outbox (envois à la plateforme agréée mis en file pendant une panne), alertes du calendrier fiscal
// (échéances proches et dépassées), passe quotidienne de la vue cabinet (compteurs de chaque dossier), factures
// récurrentes dues et relances des factures échues (envoi automatique, si un serveur d'e-mail est réglé). La plateforme
// est celle de l'API : même réglage Lettra:EInvoicing:Platform. En job de déploiement (Lettra:Worker:RunOnce), la
// migration seule (WorkerJobs).
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLettra(builder.Configuration);

// Le Worker agit sous son propre compte de service, jamais comme un utilisateur anonyme ou emprunté. Pour les factures
// récurrentes, ce compte doit avoir FactureSaisir et FactureValider dans le registre des droits (rôle manager, par
// exemple) ; sans identité configurée, la passe ne crée rien.
builder.Services.AddOptions<WorkerIdentityOptions>().Bind(builder.Configuration.GetSection(WorkerIdentityOptions.SectionName));
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser, WorkerCurrentUser>());
builder.Services.AddScoped<JobDossierSource>();
builder.Services.AddScoped<IRequestedDossierSource>(sp => sp.GetRequiredService<JobDossierSource>());
builder.Services.AddWorkerJobs(builder.Configuration);

using IHost host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
