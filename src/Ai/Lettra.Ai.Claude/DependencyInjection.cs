using Anthropic;
using Anthropic.Models.Messages;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Claude;

/// <summary>Réglages du moteur Claude (<c>Lettra:Ai:Claude</c>).</summary>
public sealed class ClaudeEngineOptions
{
    public const string SectionName = "Lettra:Ai:Claude";

    /// <summary>
    /// Clé d'API. Jamais dans un fichier versionné : secret de l'hôte (variable <c>Lettra__Ai__Claude__ApiKey</c>,
    /// coffre de la cellule) ou, à défaut, la variable standard <c>ANTHROPIC_API_KEY</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Point d'accès choisi par le cabinet (résidence des données en Europe). Vide : point d'accès par défaut du SDK.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Modèle Claude. À régler par fonction après mesure sur le banc d'évals (annexe B §5.4).</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxTokens { get; set; } = 16_000;

    /// <summary>Effort de raisonnement ; « medium » est le défaut de ce modèle, rendu explicite ici.</summary>
    public Effort Effort { get; set; } = Effort.Medium;
}

public static class DependencyInjection
{
    /// <summary>Enregistre le moteur Claude, seulement si une clé d'API est configurée sur ce déploiement.</summary>
    public static IServiceCollection AddClaudeEngine(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ClaudeEngineOptions options = configuration.GetSection(ClaudeEngineOptions.SectionName).Get<ClaudeEngineOptions>() ?? new();
        options.ApiKey ??= configuration["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return services;
        }

        services.AddKeyedSingleton<IAiEngine>(AiEngineKind.ClaudeEu, (_, _) =>
        {
            AnthropicClient client = options.BaseUrl is { } baseUrl
                ? new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = baseUrl.ToString() }
                : new AnthropicClient { ApiKey = options.ApiKey };
            return new ClaudeEngine(client, options);
        });
        return services;
    }
}
