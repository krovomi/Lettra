using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// Enregistrement de l'orchestrateur, de la politique du cabinet et de la fabrique de moteurs. Le journal IA est un
/// adapter de la persistance (base du dossier) : la couche IA ne fait qu'écrire dans son port.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAiOrchestration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>().Bind(configuration.GetSection(AiOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(INamedEntityDetection.None);

        // Remplace les implémentations « IA désactivée » enregistrées par l'application.
        services.Replace(ServiceDescriptor.Singleton<IAiUsagePolicy, CabinetAiUsagePolicy>());
        services.Replace(ServiceDescriptor.Scoped<IAccountingAssistant, AccountingAssistant>());
        services.Replace(ServiceDescriptor.Singleton<IAiEngineFactory, KeyedAiEngineFactory>());

        // Par requête : le client HTTP de la consultation web vient de la fabrique à chaque fois (rotation DNS).
        services.AddScoped(sp => new ToolRegistry(sp.GetServices<IAiTool>()));
        return services;
    }
}
