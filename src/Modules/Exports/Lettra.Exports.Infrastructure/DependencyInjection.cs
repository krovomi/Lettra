using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Exports.Application;
using Lettra.Exports.Application.AuditKit;
using Lettra.Exports.Application.Banking;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.Insights;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Application.Reminders;
using Lettra.Exports.Application.Tax;
using Lettra.Infrastructure.Persistence.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Exports.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Clé de la plateforme agréée : <c>None</c> (par défaut, rien ne part) ou <c>Simulated</c>.</summary>
    public const string PlatformKey = "Lettra:EInvoicing:Platform";

    /// <summary>
    /// Module Exports : cas d'usage, lecture du journal des ventes et transmissions dans la base du dossier (tous les
    /// modes). Sans plateforme agréée configurée (<see cref="PlatformKey"/>), la transmission échoue en
    /// <c>Platform.NotConfigured</c>.
    /// </summary>
    public static IServiceCollection AddExports(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ISalesJournalReader, EfSalesJournalReader>();
        services.AddScoped<IPlatformTransmissionStore, EfPlatformTransmissionStore>();
        services.AddScoped<IPlatformHistoryReader, EfPlatformHistoryReader>();
        services.AddScoped<IReceivablesReader, EfReceivablesReader>();
        services.AddScoped<IPaymentRepository, EfPaymentRepository>();
        services.AddScoped<IBankJournal, EfBankJournal>();
        services.AddScoped<IBankStatementRepository, EfBankStatementRepository>();
        services.AddScoped<ILetteringRepository, EfLetteringRepository>();
        services.AddScoped<IBankingSettingsRepository, EfBankingSettingsRepository>();
        services.AddScoped<IKnownIbanStore, EfKnownIbanStore>();
        services.AddScoped<IIbanFingerprints, KeyedIbanFingerprints>();
        services.AddScoped<IReceivableDocumentReader, EfReceivableDocumentReader>();
        services.AddScoped<ITaxDocumentReader, EfTaxDocumentReader>();
        services.AddScoped<IPaymentDelaysReader, EfPaymentDelaysReader>();
        services.AddScoped<IFiscalStore, EfFiscalStore>();
        services.AddScoped<IVatOnReceiptsSource, EfVatOnReceiptsSource>();
        services.AddScoped<IReminderSettingsRepository, EfReminderSettingsRepository>();
        services.AddScoped<IPaymentReminderRepository, EfPaymentReminderRepository>();
        services.AddScoped<IReminderContextReader, EfReminderContextReader>();
        services.AddScoped<IAuditTrailReader, EfAuditTrailReader>();
        services.AddScoped<IPaymentHistoryReader, EfPaymentHistoryReader>();
        services.AddScoped<IIssuedInvoiceReader, EfIssuedInvoiceReader>();
        services.AddScoped<IEInvoicingReadinessReader, EfEInvoicingReadinessReader>();
        AddPortalProviders(services, configuration);
        AddBankAggregator(services, configuration);
        services.AddScoped<IDossierPresentReader, EfDossierPresentReader>();
        string platform = configuration?[PlatformKey] ?? "None";
        switch (platform)
        {
            case "None":
                services.TryAddSingleton<IAccreditedPlatformClient, UnconfiguredPlatformClient>();
                break;
            case "Simulated":
                services.AddSimulatedAccreditedPlatform();
                break;
            default:
                throw new InvalidOperationException($"{PlatformKey} : « {platform} » inconnu (None ou Simulated).");
        }

        return services.AddExportsApplication();
    }

    /// <summary>Clé de l'agrégateur des banques connectées : <c>Simulated</c> (par défaut) ou <c>None</c> (import de relevés seulement).</summary>
    public const string BankAggregatorKey = "Lettra:BankConnections:Aggregator";

    /// <summary>Banques connectées : connexions dans la base du dossier, agrégateur simulé (ou aucun, <see cref="BankAggregatorKey"/>).</summary>
    private static void AddBankAggregator(IServiceCollection services, IConfiguration? configuration)
    {
        services.AddScoped<IBankConnectionRepository, EfBankConnectionRepository>();
        string aggregator = configuration?[BankAggregatorKey] ?? "Simulated";
        switch (aggregator)
        {
            case "Simulated":
                services.TryAddScoped<IBankAggregator, SimulatedBankAggregator>();
                break;
            case "None":
                services.TryAddSingleton<IBankAggregator, NoBankAggregator>();
                break;
            default:
                throw new InvalidOperationException($"{BankAggregatorKey} : « {aggregator} » inconnu (Simulated ou None).");
        }
    }

    /// <summary>Clé du prestataire de paiement en ligne de l'espace client : <c>Simulated</c> (par défaut) ou <c>None</c>.</summary>
    public const string OnlinePaymentKey = "Lettra:CustomerPortal:Payments";

    /// <summary>
    /// Espace client : liens et preuves de signature dans la base du dossier, prestataire de paiement simulé (ou aucun,
    /// <see cref="OnlinePaymentKey"/>) et signature électronique simulée. Rien ne sort du processus.
    /// </summary>
    private static void AddPortalProviders(IServiceCollection services, IConfiguration? configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPortalLinkRepository, EfPortalLinkRepository>();
        services.AddScoped<IQuoteSignatureRepository, EfQuoteSignatureRepository>();
        services.TryAddSingleton<IElectronicSignatureProvider, SimulatedSignatureProvider>();
        string payments = configuration?[OnlinePaymentKey] ?? "Simulated";
        switch (payments)
        {
            case "Simulated":
                services.TryAddSingleton<SimulatedPaymentProvider>();
                services.TryAddSingleton<IOnlinePaymentProvider>(sp => sp.GetRequiredService<SimulatedPaymentProvider>());
                break;
            case "None":
                services.TryAddSingleton<IOnlinePaymentProvider, NoOnlinePayment>();
                break;
            default:
                throw new InvalidOperationException($"{OnlinePaymentKey} : « {payments} » inconnu (Simulated ou None).");
        }
    }

    /// <summary>La plateforme agréée simulée, en mémoire (démonstration et tests ; données fictives).</summary>
    public static IServiceCollection AddSimulatedAccreditedPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.RemoveAll<IAccreditedPlatformClient>();
        services.TryAddSingleton<SimulatedAccreditedPlatform>();
        services.AddSingleton<IAccreditedPlatformClient>(sp => sp.GetRequiredService<SimulatedAccreditedPlatform>());
        return services;
    }

    /// <summary>
    /// Export FEC en masse par le Worker (<c>Lettra:Worker:FecExport</c>, désactivé par défaut) ; l'hôte fournit la liste
    /// des dossiers actifs.
    /// </summary>
    public static IServiceCollection AddFecBulkExport(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<FecBulkExportOptions>().Bind(configuration.GetSection(FecBulkExportOptions.SectionName))
            .Validate(o => o.InitialDelay >= TimeSpan.Zero, $"{FecBulkExportOptions.SectionName}:{nameof(FecBulkExportOptions.InitialDelay)} ne peut pas être négatif.")
            .ValidateOnStart();
        services.AddSingleton(new ActiveDossierSource(activeDossiers));
        services.AddSingleton<IExportArchive, FileSystemExportArchive>();
        services.AddSingleton<FecBulkExportJob>();
        services.AddHostedService(sp => sp.GetRequiredService<FecBulkExportJob>());
        return services;
    }

    /// <summary>
    /// Passe d'alertes du calendrier fiscal par le Worker (<c>Lettra:Worker:FiscalCalendar</c>, active par défaut) :
    /// échéances proches (S3) et dépassées (S2) de chaque dossier actif, vers le centre d'alertes.
    /// </summary>
    public static IServiceCollection AddFiscalDeadlineAlerts(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<FiscalDeadlineAlertOptions>().Bind(configuration.GetSection(FiscalDeadlineAlertOptions.SectionName)).ValidatePeriodicJob(FiscalDeadlineAlertOptions.SectionName);
        services.AddSingleton(new FiscalDossierSource(activeDossiers));
        services.AddSingleton<FiscalDeadlineAlertJob>();
        services.AddHostedService(sp => sp.GetRequiredService<FiscalDeadlineAlertJob>());
        return services;
    }

    /// <summary>
    /// Passe quotidienne des relances par le Worker (<c>Lettra:Worker:PaymentReminders</c>, active par défaut) : envoie
    /// les relances dues des dossiers en envoi automatique, si un serveur d'e-mail est configuré (<c>Lettra:Email</c>).
    /// </summary>
    public static IServiceCollection AddPaymentReminders(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TenantDescriptor>>> activeDossiers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<PaymentReminderJobOptions>().Bind(configuration.GetSection(PaymentReminderJobOptions.SectionName)).ValidatePeriodicJob(PaymentReminderJobOptions.SectionName);
        services.AddSingleton(new ReminderDossierSource(activeDossiers));
        services.AddSingleton<PaymentReminderJob>();
        services.AddHostedService(sp => sp.GetRequiredService<PaymentReminderJob>());
        return services;
    }
}
