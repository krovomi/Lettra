using Anthropic;
using Anthropic.Bedrock;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Anthropic.Vertex;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Claude;

/// <summary>Fournisseur qui sert Claude à ce déploiement.</summary>
public enum ClaudeProvider
{
    /// <summary>Aucun : le moteur Claude n'existe pas sur ce déploiement (réglage par défaut).</summary>
    None = 0,

    /// <summary>Amazon Bedrock, dans une région UE (<c>eu-*</c>), identifiants AWS du déploiement.</summary>
    Bedrock = 1,

    /// <summary>Google Vertex AI, dans une région UE (<c>europe-*</c> ou la multirégion <c>eu</c>), identifiants Google du déploiement.</summary>
    Vertex = 2,

    /// <summary>API directe d'Anthropic, hors UE : réservée aux évaluations sur données fictives, jamais en production.</summary>
    AnthropicDirect = 3,
}

/// <summary>Réglages du moteur Claude (<c>Lettra:Ai:Claude</c>).</summary>
public sealed class ClaudeEngineOptions
{
    public const string SectionName = "Lettra:Ai:Claude";

    public ClaudeProvider Provider { get; set; } = ClaudeProvider.None;

    /// <summary>Région du fournisseur : <c>eu-west-3</c> (Paris) pour Bedrock, <c>europe-west9</c> (Paris) pour Vertex.</summary>
    public string? Region { get; set; }

    /// <summary>Projet Google Cloud (Vertex seulement).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Profil AWS nommé (Bedrock seulement) ; sans lui, la chaîne d'identifiants standard d'AWS.</summary>
    public string? AwsProfile { get; set; }

    /// <summary>
    /// Clé de l'API directe d'Anthropic (<see cref="ClaudeProvider.AnthropicDirect"/>). Jamais dans un fichier versionné :
    /// secret de l'hôte ou variable <c>ANTHROPIC_API_KEY</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Autorise l'API directe d'Anthropic, hors UE. Réservé aux évaluations sur données fictives ; jamais en production.</summary>
    public bool AllowNonEuEndpoint { get; set; }

    /// <summary>
    /// Modèle Claude, identifiant de l'API d'Anthropic. Sur Bedrock, le préfixe <c>anthropic.</c> est ajouté s'il manque.
    /// À régler par fonction après mesure sur le banc d'évals (annexe B §5.4).
    /// </summary>
    public string Model { get; set; } = "claude-opus-5-5";

    public int MaxTokens { get; set; } = 16_000;

    /// <summary>Effort de raisonnement ; « medium » est le défaut de ce modèle, rendu explicite ici.</summary>
    public Effort Effort { get; set; } = Effort.Medium;

    /// <summary>Identifiant du modèle tel que le fournisseur l'attend.</summary>
    public string ProviderModel => Provider == ClaudeProvider.Bedrock && !Model.Contains('.', StringComparison.Ordinal) ? "anthropic." + Model : Model;
}

/// <summary>Identifiants explicites d'un fournisseur ; en production ils viennent de l'environnement du déploiement.</summary>
internal sealed record ProviderCredentials(
    string? AwsAccessKey = null,
    string? AwsSecretAccessKey = null,
    Google.Apis.Auth.OAuth2.GoogleCredential? Google = null);

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre le moteur Claude, seulement si un fournisseur est configuré sur ce déploiement. Échec fermé : une région
    /// hors UE, un projet Vertex absent ou l'API directe sans autorisation d'évaluation empêchent le démarrage, au lieu
    /// d'envoyer des données hors de l'UE (ADR-0012).
    /// </summary>
    public static IServiceCollection AddClaudeEngine(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ClaudeEngineOptions options = configuration.GetSection(ClaudeEngineOptions.SectionName).Get<ClaudeEngineOptions>() ?? new();
        options.ApiKey ??= configuration["ANTHROPIC_API_KEY"];
        if (Validate(options, configuration.GetSection(ClaudeEngineOptions.SectionName)["BaseUrl"]) is { } refusal)
        {
            throw new InvalidOperationException($"Moteur Claude refusé : {refusal}");
        }

        if (options.Provider == ClaudeProvider.None)
        {
            return services;
        }

        services.AddKeyedSingleton<IAiEngine>(AiEngineKind.ClaudeEu, (_, _) => new ClaudeEngine(CreateClient(options, new ClientOptions()), options));
        return services;
    }

    /// <summary>
    /// Client du fournisseur choisi. <paramref name="client"/> porte les réglages communs (client HTTP, nouvelles tentatives) ;
    /// sans <paramref name="credentials"/>, les identifiants viennent de la chaîne standard d'AWS ou de Google (ADC) du
    /// déploiement. Les tests y mettent un faux serveur et de faux identifiants.
    /// </summary>
    internal static IAnthropicClient CreateClient(ClaudeEngineOptions options, ClientOptions client, ProviderCredentials? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Provider switch
        {
            ClaudeProvider.Bedrock => new AnthropicBedrockMantleClient(new MantleAwsClientOptions
            {
                AwsRegion = options.Region,
                AwsProfile = options.AwsProfile,
                AwsAccessKey = credentials?.AwsAccessKey,
                AwsSecretAccessKey = credentials?.AwsSecretAccessKey,
                ClientOptions = client,
            }),
            ClaudeProvider.Vertex => new AnthropicVertexClient(credentials?.Google is { } google
                ? new AnthropicVertexCredentials(options.Region!, options.ProjectId!, google)
                : new AnthropicVertexCredentials(options.Region!, options.ProjectId!))
            {
                HttpClient = client.HttpClient ?? new HttpClient(),
                MaxRetries = client.MaxRetries,
            },
            ClaudeProvider.AnthropicDirect => new AnthropicClient(client) { ApiKey = options.ApiKey },
            _ => throw new InvalidOperationException("Aucun fournisseur Claude configuré."),
        };
    }

    /// <summary>La raison qui empêche de démarrer le moteur, ou <c>null</c>.</summary>
    internal static string? Validate(ClaudeEngineOptions options, string? legacyBaseUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(legacyBaseUrl))
        {
            return $"{ClaudeEngineOptions.SectionName}:BaseUrl n'est plus lu : choisissez Provider (Bedrock ou Vertex) et une région UE.";
        }

        return options.Provider switch
        {
            ClaudeProvider.None when !string.IsNullOrWhiteSpace(options.ApiKey) =>
                $"une clé d'API sans fournisseur. {ClaudeEngineOptions.SectionName}:Provider doit désigner un fournisseur en région UE " +
                "(Bedrock, Vertex) : l'API directe d'Anthropic n'offre pas de résidence des données dans l'UE.",
            ClaudeProvider.None => null,
            ClaudeProvider.Bedrock when !IsEuBedrockRegion(options.Region) =>
                $"la région Bedrock « {options.Region} » n'est pas une région UE (eu-west-3, eu-central-1…).",
            ClaudeProvider.Vertex when !IsEuVertexRegion(options.Region) =>
                $"la région Vertex « {options.Region} » n'est pas une région UE (europe-west9, eu…). « global » ne garantit pas la résidence.",
            ClaudeProvider.Vertex when string.IsNullOrWhiteSpace(options.ProjectId) => "Vertex demande ProjectId, le projet Google Cloud.",
            ClaudeProvider.AnthropicDirect when !options.AllowNonEuEndpoint =>
                "l'API directe d'Anthropic, hors région UE, n'est acceptée qu'avec AllowNonEuEndpoint, pour les évaluations sur données fictives.",
            ClaudeProvider.AnthropicDirect when string.IsNullOrWhiteSpace(options.ApiKey) => "l'API directe d'Anthropic demande une clé d'API.",
            _ => null,
        };
    }

    private static bool IsEuBedrockRegion(string? region) =>
        region is { Length: > 3 } r && r.StartsWith("eu-", StringComparison.Ordinal) && r.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static bool IsEuVertexRegion(string? region) =>
        region is "eu" || (region is { Length: > 7 } r && r.StartsWith("europe-", StringComparison.Ordinal) && r.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'));
}
