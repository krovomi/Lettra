using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Catalog;
using Lettra.Infrastructure.Persistence.Billing;
using Lettra.Infrastructure.Persistence.Cabinet;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Outbox;
using Lettra.Supervision.Infrastructure;

namespace Lettra.Worker;

/// <summary>Les jobs de fond du Worker, selon qu'il tourne en continu ou en job de déploiement (<c>RunOnce</c>).</summary>
internal static class WorkerJobs
{
    /// <summary>
    /// Migration du catalogue et des bases de dossiers, toujours. Lancé en job de déploiement (<c>RunOnce</c>), le Worker
    /// ne fait que migrer puis s'arrête : aucune autre passe n'est enregistrée, il n'émet ni facture ni e-mail, ne
    /// transmet rien à la plateforme et ne lève aucune alerte.
    /// </summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DossierMigrationJobOptions>().Bind(configuration.GetSection(DossierMigrationJobOptions.SectionName))
            .Validate(o => o.Interval > TimeSpan.Zero, $"{DossierMigrationJobOptions.SectionName}:{nameof(DossierMigrationJobOptions.Interval)} doit être strictement positif.")
            .ValidateOnStart();
        services.AddHostedService<DossierMigrationJob>();
        if (configuration.GetValue<bool>($"{DossierMigrationJobOptions.SectionName}:{nameof(DossierMigrationJobOptions.RunOnce)}"))
        {
            return services;
        }

        services.AddEscalationJob(configuration);
        services.AddIntegrityCheckJob(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddFecBulkExport(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddOutboxDispatch(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddFiscalDeadlineAlerts(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddCabinetOverviewJob(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddRecurringInvoicing(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        services.AddPaymentReminders(configuration, (sp, token) => sp.ListActiveDossiersAsync(token));
        return services;
    }
}
