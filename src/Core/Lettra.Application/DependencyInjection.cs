using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Authorization;
using Lettra.Application.Behaviors;
using Lettra.Application.Messaging;
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
        services.TryAddSingleton<IAiSettingsStore, NoAiSettingsStore>();
        services.TryAddScoped<IAccountingAssistant, UnavailableAccountingAssistant>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        // Hors du mode 1, les cas d'usage de session locale répondent « non pris en charge ».
        services.TryAddSingleton<ILocalAccounts, NoLocalAccounts>();

        // Ordre = de l'extérieur vers l'intérieur : on trace tout, puis on vérifie le dossier,
        // puis les droits, puis la saisie. Un refus de droits ne révèle donc rien de la validation.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TenantGuardBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

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
