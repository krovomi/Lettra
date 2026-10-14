using Lettra.Ai.Claude;
using Lettra.Ai.Local;
using Lettra.Ai.Orchestration;
using Lettra.Ai.Skills;
using Lettra.Ai.Tools;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Bootstrap;

/// <summary>
/// Composition de la couche IA (annexe B §4.1). Elle est enregistrée dans tous les modes, désactivée par défaut :
/// la politique du cabinet (<c>Lettra:Ai</c>) choisit le moteur, et un moteur n'existe que si sa clé ou son point
/// d'accès est configuré sur ce déploiement.
/// </summary>
public static class LettraAiBootstrap
{
    public static IServiceCollection AddLettraAi(this IServiceCollection services, IConfiguration configuration)
    {
        // Le skill pack est vérifié (SHA-256) au premier usage : un pack altéré empêche l'assistant de répondre.
        services.AddSingleton<IAiSkillCatalog>(_ => SkillCatalog.LoadEmbedded());
        services.AddAiTools();
        services.AddAiOrchestration(configuration);
        services.AddClaudeEngine(configuration);
        services.AddLocalAiEngine(configuration);
        return services;
    }
}
