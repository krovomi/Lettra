using System.Text.Json;
using Anthropic.Core;
using Google.Apis.Auth.OAuth2;
using Lettra.Ai.Claude;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tests;

/// <summary>
/// Claude en région UE (ADR-0012) : Amazon Bedrock et Google Vertex AI, testés contre des réponses enregistrées, sans
/// compte ni clé. Le moteur n'existe que si un fournisseur est configuré, et il refuse de démarrer hors UE (échec fermé).
/// </summary>
public sealed class ClaudeEuProviderTests
{
    private const string ToolUseResponse = """
    {
      "id": "msg_01", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
      "content": [
        { "type": "text", "text": "Je vérifie." },
        { "type": "tool_use", "id": "toolu_01", "name": "calculer", "input": { "operation": "cca" } }
      ],
      "stop_reason": "tool_use", "stop_sequence": null,
      "usage": { "input_tokens": 120, "output_tokens": 30, "cache_read_input_tokens": 4000, "cache_creation_input_tokens": 0 }
    }
    """;

    private static readonly ProviderCredentials FakeCredentials = new("AKIDEXAMPLE", "secret-de-test", GoogleCredential.FromAccessToken("jeton-de-test"));

    [Fact]
    public async Task Bedrock_signe_la_requete_pour_la_region_UE_et_traduit_la_reponse()
    {
        (EngineTurn turn, HttpRequestMessage request, JsonElement body) = await CallAsync(
            new ClaudeEngineOptions { Provider = ClaudeProvider.Bedrock, Region = "eu-west-3" });

        request.RequestUri.Should().Be(new Uri("https://bedrock-mantle.eu-west-3.api.aws/anthropic/v1/messages"));
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should()
            .StartWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/").And.Contain("/eu-west-3/bedrock-mantle/aws4_request");
        request.Headers.Contains("x-api-key").Should().BeFalse("aucune clé Anthropic sur Bedrock");
        body.GetProperty("model").GetString().Should().Be("anthropic.claude-opus-5-5");
        body.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        body.GetProperty("tools")[0].GetProperty("strict").GetBoolean().Should().BeTrue();
        AssertToolUse(turn);
    }

    [Fact]
    public async Task Vertex_appelle_la_region_UE_du_projet_avec_le_jeton_Google_et_traduit_la_reponse()
    {
        (EngineTurn turn, HttpRequestMessage request, JsonElement body) = await CallAsync(
            new ClaudeEngineOptions { Provider = ClaudeProvider.Vertex, Region = "europe-west9", ProjectId = "lettra-test" });

        request.RequestUri.Should().Be(new Uri(
            "https://europe-west9-aiplatform.googleapis.com/v1/projects/lettra-test/locations/europe-west9/publishers/anthropic/models/claude-opus-5-5:rawPredict"));
        request.Headers.Authorization.Should().Be(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "jeton-de-test"));
        body.GetProperty("anthropic_version").GetString().Should().Be("vertex-2023-10-16");
        body.TryGetProperty("model", out _).Should().BeFalse("le modèle est dans l'adresse sur Vertex");
        AssertToolUse(turn);
    }

    [Fact]
    public void Sans_fournisseur_configure_le_moteur_Claude_n_existe_pas()
    {
        ServiceCollection services = new();
        services.AddClaudeEngine(Configuration([]));

        services.Should().NotContain(d => d.ServiceType == typeof(IAiEngine));
    }

    [Theory]
    [InlineData("Bedrock", "eu-west-3", null)]
    [InlineData("Bedrock", "eu-central-1", null)]
    [InlineData("Vertex", "europe-west9", "lettra-prod")]
    [InlineData("Vertex", "eu", "lettra-prod")]
    public void Un_fournisseur_en_region_UE_enregistre_le_moteur(string provider, string region, string? project)
    {
        ServiceCollection services = new();
        services.AddClaudeEngine(Configuration(new()
        {
            ["Lettra:Ai:Claude:Provider"] = provider,
            ["Lettra:Ai:Claude:Region"] = region,
            ["Lettra:Ai:Claude:ProjectId"] = project,
        }));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IAiEngine) && Equals(d.ServiceKey, AiEngineKind.ClaudeEu));
    }

    [Theory]
    [InlineData("Bedrock", "us-east-1", null, "*n'est pas une région UE*")]
    [InlineData("Bedrock", null, null, "*n'est pas une région UE*")]
    [InlineData("Bedrock", "eu-west-3.evil.example/", null, "*n'est pas une région UE*")]
    [InlineData("Vertex", "global", "lettra-prod", "*« global » ne garantit pas la résidence*")]
    [InlineData("Vertex", "us-east5", "lettra-prod", "*n'est pas une région UE*")]
    [InlineData("Vertex", "europe-west9", null, "*ProjectId*")]
    [InlineData("AnthropicDirect", null, null, "*AllowNonEuEndpoint*")]
    public void Hors_UE_ou_incomplet_le_moteur_Claude_refuse_de_demarrer(string provider, string? region, string? project, string message)
    {
        IConfiguration configuration = Configuration(new()
        {
            ["Lettra:Ai:Claude:Provider"] = provider,
            ["Lettra:Ai:Claude:Region"] = region,
            ["Lettra:Ai:Claude:ProjectId"] = project,
            ["ANTHROPIC_API_KEY"] = "cle-de-test",
        });

        FluentActions.Invoking(() => new ServiceCollection().AddClaudeEngine(configuration))
            .Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Theory]
    [InlineData("ANTHROPIC_API_KEY", "cle-de-test", "*sans fournisseur*")]
    [InlineData("Lettra:Ai:Claude:ApiKey", "cle-de-test", "*sans fournisseur*")]
    [InlineData("Lettra:Ai:Claude:BaseUrl", "https://bedrock-runtime.eu-west-3.amazonaws.com", "*BaseUrl n'est plus lu*")]
    public void Une_cle_ou_une_adresse_sans_fournisseur_ne_bascule_jamais_vers_l_API_directe(string key, string value, string message) =>
        FluentActions.Invoking(() => new ServiceCollection().AddClaudeEngine(Configuration(new() { [key] = value })))
            .Should().Throw<InvalidOperationException>().WithMessage(message);

    [Fact]
    public void L_API_directe_n_est_acceptee_que_sur_autorisation_explicite_pour_les_evaluations()
    {
        ServiceCollection services = new();
        services.AddClaudeEngine(Configuration(new()
        {
            ["Lettra:Ai:Claude:Provider"] = "AnthropicDirect",
            ["Lettra:Ai:Claude:AllowNonEuEndpoint"] = "true",
            ["ANTHROPIC_API_KEY"] = "cle-de-test",
        }));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetKeyedService<IAiEngine>(AiEngineKind.ClaudeEu).Should().BeOfType<ClaudeEngine>().Which.Name.Should().Be("claude:claude-opus-5-5");
    }

    private static async Task<(EngineTurn Turn, HttpRequestMessage Request, JsonElement Body)> CallAsync(ClaudeEngineOptions options)
    {
        RecordingHandler handler = new(_ => RecordingHandler.Json(ToolUseResponse));
        ClientOptions client = new() { HttpClient = new HttpClient(handler), MaxRetries = 0 };
        ClaudeEngine engine = new(DependencyInjection.CreateClient(options, client, FakeCredentials), options);
        JsonElement schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { operation = new { type = "string" } },
            required = new[] { "operation" },
            additionalProperties = false,
        });

        EngineTurn turn = await engine.CompleteAsync(
            new EngineRequest("SURCOUCHE", "CONTEXTE", [EngineMessage.UserText("Pourquoi ?")], [new EngineToolDefinition("calculer", "Calcul", schema)]),
            CancellationToken.None);

        (HttpRequestMessage request, string body) = handler.Requests.Should().ContainSingle().Which;
        return (turn, request, JsonDocument.Parse(body).RootElement.Clone());
    }

    private static void AssertToolUse(EngineTurn turn)
    {
        turn.StopReason.Should().Be(EngineStopReason.ToolUse);
        ToolCallPart call = turn.ToolCalls.Should().ContainSingle().Which;
        call.Name.Should().Be("calculer");
        call.Input.GetProperty("operation").GetString().Should().Be("cca");
        turn.Usage!.CacheReadTokens.Should().Be(4000);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
