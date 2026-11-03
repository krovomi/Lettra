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
        services.TryAddSingleton(INamedEntityDetection.None);
        services.TryAddScoped<IAccountingAssistant, UnavailableAccountingAssistant>();
        services.TryAddScoped<IMcpServer, NoMcpServer>();

        // Voix désactivée tant que le déploiement ne choisit pas de moteur (Lettra.Infrastructure.Speech, ou Whisper sur le poste).
        services.TryAddSingleton(SpeechSettings.Disabled);
        services.TryAddSingleton<ISpeechToText, UnavailableSpeechToText>();

        // E-mails : coupés tant que l'infrastructure ne branche pas un serveur SMTP (Lettra:Email).
        services.TryAddSingleton<IEmailSender, NoEmailSender>();
        services.TryAddSingleton(new LettraPublicAddress(null));

        // Vérification du bénéficiaire : impossible tant que l'infrastructure ne branche pas de banque (même simulée).
        services.TryAddSingleton<Abstractions.External.IPayeeVerification, Abstractions.External.NoPayeeVerification>();

        // Registres officiels (bénéficiaires effectifs, gel des avoirs, BODACC, URSSAF) : injoignables tant que
        // l'infrastructure n'en branche pas (même simulés).
        services.TryAddSingleton<Abstractions.External.NoOfficialRegisters>();
        services.TryAddSingleton<Abstractions.External.IBeneficialOwnerRegister>(sp => sp.GetRequiredService<Abstractions.External.NoOfficialRegisters>());
        services.TryAddSingleton<Abstractions.External.IAssetFreezeRegister>(sp => sp.GetRequiredService<Abstractions.External.NoOfficialRegisters>());
        services.TryAddSingleton<Abstractions.External.ILegalNoticeFeed>(sp => sp.GetRequiredService<Abstractions.External.NoOfficialRegisters>());
        services.TryAddSingleton<Abstractions.External.IUrssafAttestationCheck>(sp => sp.GetRequiredService<Abstractions.External.NoOfficialRegisters>());

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
        services.AddScoped<Features.Parties.IbanChangeDecisions>();
        services.AddScoped<Features.Parties.ThirdPartyRegisters>();

        // Décisions à prendre : l'actualisation, l'indicateur de la vue cabinet (qui l'appelle à la passe quotidienne) et
        // les règles du cœur ; les modules ajoutent les leurs et leurs actions (docs/decisions-a-prendre.md).
        services.AddScoped<Features.Decisions.DecisionRefresher>();
        services.AddScoped<Abstractions.Cabinet.IDossierIndicatorSource, Features.Decisions.DecisionsIndicatorSource>();
        services.AddScoped<Abstractions.Decisions.IDecisionRule, Features.Decisions.IbanChangeDecisionRule>();
        services.AddScoped<Abstractions.Decisions.IDecisionRule, Features.Decisions.AttestationDecisionRule>();
        services.AddScoped<Abstractions.Decisions.IDecisionRule, Features.Decisions.DueDiligenceDecisionRule>();

        // Vue cabinet : les dossiers accessibles, partagés par le sélecteur et le tableau de supervision.
        services.AddScoped<Features.Dossiers.AccessibleDossiers>();
        services.AddScoped<Features.Cabinet.CabinetPeopleReader>();

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
