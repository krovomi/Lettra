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

    /// <summary>
    /// Point d'accès en région UE (Amazon Bedrock, Google Vertex AI). Obligatoire : l'API directe d'Anthropic n'offre pas
    /// de résidence des données dans l'UE, et Lettra refuse de démarrer plutôt que d'y envoyer des données (ADR-0012).
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Autorise l'API directe d'Anthropic, hors UE. Réservé aux évaluations sur données fictives ; jamais en production.
    /// </summary>
    public bool AllowNonEuEndpoint { get; set; }

    /// <summary>Modèle Claude. À régler par fonction après mesure sur le banc d'évals (annexe B §5.4).</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxTokens { get; set; } = 16_000;

    /// <summary>Effort de raisonnement ; « medium » est le défaut de ce modèle, rendu explicite ici.</summary>
    public Effort Effort { get; set; } = Effort.Medium;
}

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre le moteur Claude, seulement si une clé d'API est configurée sur ce déploiement. Échec fermé : avec une
    /// clé mais sans point d'accès en région UE, le démarrage échoue au lieu de basculer vers l'API directe, hors UE.
    /// </summary>
    public static IServiceCollection AddClaudeEngine(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ClaudeEngineOptions options = configuration.GetSection(ClaudeEngineOptions.SectionName).Get<ClaudeEngineOptions>() ?? new();
        options.ApiKey ??= configuration["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return services;
        }

        if (!options.AllowNonEuEndpoint && !IsEuCloudEndpoint(options.BaseUrl))
        {
            throw new InvalidOperationException(
                $"Moteur Claude refusé : {ClaudeEngineOptions.SectionName}:BaseUrl doit désigner un point d'accès en région UE " +
                "(Amazon Bedrock, Google Vertex AI). L'API directe d'Anthropic n'offre pas de résidence des données dans l'UE.");
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

    /// <summary>Un point d'accès https explicite, autre que l'API directe d'Anthropic.</summary>
    private static bool IsEuCloudEndpoint(Uri? baseUrl) =>
        baseUrl is { IsAbsoluteUri: true } url
        && url.Scheme == Uri.UriSchemeHttps
        && !url.Host.Equals("anthropic.com", StringComparison.OrdinalIgnoreCase)
        && !url.Host.EndsWith(".anthropic.com", StringComparison.OrdinalIgnoreCase);
}
