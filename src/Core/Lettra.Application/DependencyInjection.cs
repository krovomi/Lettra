using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Preferences;
using Lettra.Application.Abstractions.Speech;
using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Authorization;
using Lettra.Application.Behaviors;
using Lettra.Application.Features.Integrity;
using Lettra.Application.Messaging;
using Lettra.Domain.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Application;

/// <summary>Enregistrement de la couche application : dispatchers, pipeline, handlers et validateurs.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Assistant IA désactivé tant que la couche IA (Lettra.Ai.*) n'est pas composée.
        services.TryAddSingleton<IAiUsagePolicy, DisabledAiUsagePolicy>();
        services.TryAddSingleton<IAiAuditLog, EmptyAiAuditLog>();
        services.TryAddSingleton<IAiEngineFactory, NoAiEngineFactory>();
        services.TryAddSingleton<IAiSettingsStore, NoAiSettingsStore>();
        services.TryAddScoped<IAccountingAssistant, UnavailableAccountingAssistant>();

        // Voix désactivée tant que le déploiement ne choisit pas de moteur (Lettra.Infrastructure.Speech, ou Whisper sur le poste).
        services.TryAddSingleton(SpeechSettings.Disabled);
        services.TryAddSingleton<ISpeechToText, UnavailableSpeechToText>();

        // E-mails : coupés tant que l'infrastructure ne branche pas un serveur SMTP (Lettra:Email).
        services.TryAddSingleton<IEmailSender, NoEmailSender>();
        services.TryAddSingleton(new LettraPublicAddress(null));

        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        // Chaîne anti-fraude : le module des pièces fournit son lecteur, le module Supervision reçoit les alertes.
        services.TryAddSingleton(ClosingSchedule.Paris);
        services.TryAddScoped<IJournaledDocumentReader, NotConnectedDocumentReader>();
        services.TryAddScoped<IDossierAlertPublisher, NoDossierAlertPublisher>();
        services.TryAddScoped<IDeviceNumbering, NoDeviceNumbering>();
        services.TryAddScoped<IDeviceSeriesRegistry, NoDeviceSeriesRegistry>();
        services.TryAddScoped<ISyncDeviceRegistry, NoSyncDeviceRegistry>();
        services.AddScoped<DossierIntegrityService>();

        // Vue cabinet : les dossiers accessibles, partagés par le sélecteur et le tableau de supervision.
        services.AddScoped<Features.Dossiers.AccessibleDossiers>();

        // Hors du mode 1, les cas d'usage de session locale répondent « non pris en charge ».
        services.TryAddSingleton<ILocalAccounts, NoLocalAccounts>();

        // Sans base Catalog (poste isolé), les préférences de l'utilisateur restent sur l'appareil.
        services.TryAddSingleton<IUserPreferencesStore, NoUserPreferencesStore>();

        // Sans base Catalog (poste isolé, registre en configuration), les droits ne s'administrent pas.
        services.TryAddSingleton<IAuthorizationAdministration, NoAuthorizationAdministration>();
        services.TryAddSingleton<IAuthorizationRegistry, NoAuthorizationRegistry>();
        services.TryAddSingleton<IPermissionCacheInvalidator, NoPermissionCache>();

        // Séparation des tâches (annexe F §3.2) : la politique vient de la base Catalog hors du mode 1.
        services.TryAddSingleton<IOrganisationPolicyStore, NoOrganisationPolicies>();
        services.AddScoped<ISegregationOfDuties, SegregationOfDutiesService>();

        // Ordre = de l'extérieur vers l'intérieur : on trace tout, puis on vérifie le dossier,
        // puis les droits, puis la saisie. Un refus de droits ne révèle donc rien de la validation.
        // Au plus près du handler, l'écriture concurrente détectée à l'enregistrement devient un échec (ADR-0013).
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TenantGuardBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ConcurrencyBehavior<,>));

        services.AddScoped<Features.Administration.InvitationMailer>();
        RegisterImplementations(services, typeof(ICommandHandler<,>));
        RegisterImplementations(services, typeof(IQueryHandler<,>));
        RegisterImplementations(services, typeof(IValidator<>));
        return services;
    }

    private static void RegisterImplementations(IServiceCollection services, Type openInterface)
    {
        IEnumerable<(Type Service, Type Implementation)> pairs =
            from type in typeof(DependencyInjection).Assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false }
            from service in type.GetInterfaces()
            where service.IsGenericType && service.GetGenericTypeDefinition() == openInterface
            select (service, type);

        foreach ((Type service, Type implementation) in pairs)
        {
            services.AddScoped(service, implementation);
        }
    }
}
