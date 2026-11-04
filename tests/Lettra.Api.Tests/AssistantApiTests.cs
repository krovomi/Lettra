using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>L'assistant IA par l'API : désactivé par défaut, puis un flux NDJSON quand le cabinet l'active.</summary>
public sealed class AssistantApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static readonly AskAssistantRequest Question = new()
    {
        Feature = AssistantFeature.RegulatoryQuestions,
        Question = "Quel est le taux de TVA sur une prestation de conseil pour Boulangerie Martin ?",
    };

    [Fact]
    public async Task Sans_activation_par_le_cabinet_l_assistant_repond_403_Ai_Disabled()
    {
        using HttpClient client = Client(factory);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/assistant", Question, ContractsJsonContext.Default.AskAssistantRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ProblemResponse problem = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))!;
        problem.Code.Should().Be(ErrorCodes.Ai.Disabled);
    }

    [Fact]
    public async Task Une_question_vide_est_refusee_avant_l_assistant()
    {
        using HttpClient client = Client(factory);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/assistant", new AskAssistantRequest { Feature = AssistantFeature.RegulatoryQuestions, Question = " " }, ContractsJsonContext.Default.AskAssistantRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Une_fois_active_l_assistant_repond_en_flux_et_trace_l_echange_dans_le_journal_du_dossier()
    {
        EchoEngine engine = new();
        using WebApplicationFactory<Program> enabled = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lettra:Ai:Enabled"] = "true",
                ["Lettra:Ai:Engine"] = nameof(AiEngineKind.ClaudeEu),
                ["Lettra:Ai:Features:0"] = nameof(AssistantFeature.RegulatoryQuestions),
            }));
            builder.ConfigureTestServices(services => services.AddKeyedSingleton<IAiEngine>(AiEngineKind.ClaudeEu, engine));
        });
        using HttpClient client = Client(enabled);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/assistant", Question, ContractsJsonContext.Default.AskAssistantRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/x-ndjson");
        string[] lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        List<AssistantEvent> events = [.. lines.Select(l => JsonSerializer.Deserialize(l, ContractsJsonContext.Default.AssistantEvent)!)];
        events.OfType<AssistantTextEvent>().Should().ContainSingle().Which.Text.Should().Contain("20 %");
        events[^1].Should().BeOfType<AssistantCompletedEvent>().Which.Engine.Should().Be("test:echo");

        // Article 50 de l'AI Act : le texte de l'IA porte une marque lisible par machine et son moteur.
        using JsonDocument text = JsonDocument.Parse(lines.Single(l => l.Contains("\"type\":\"text\"", StringComparison.Ordinal)));
        text.RootElement.GetProperty("aiGenerated").GetBoolean().Should().BeTrue();
        text.RootElement.GetProperty("digitalSourceType").GetString().Should().Be(AssistantTextEvent.TrainedAlgorithmicMedia);
        text.RootElement.GetProperty("engine").GetString().Should().Be("test:echo");

        // Le journal du dossier garde la trace de la demande et de la réponse.
        engine.LastQuestion.Should().NotBeNull().And.Contain("conseil");
        IReadOnlyList<AssistantJournalEntry> journal = (await client.GetFromJsonAsync("/api/v1/assistant/journal", ContractsJsonContext.Default.IReadOnlyListAssistantJournalEntry))!;
        journal.Should().Contain(e => e.Kind == nameof(AiAuditKind.Request)).And.Contain(e => e.Kind == nameof(AiAuditKind.Answer));
    }

    [Fact]
    public async Task Un_moteur_en_panne_termine_le_flux_sans_erreur_puis_Lettra_cesse_de_l_appeler()
    {
        BrokenEngine engine = new();
        using WebApplicationFactory<Program> enabled = Enabled(engine);
        using HttpClient client = Client(enabled);

        for (int i = 0; i < 3; i++)
        {
            (await AskAsync(client))[^1].Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EngineFailed), "la réponse se termine proprement, en 200");
        }

        // « IA disponible » : le statut dit que le moteur est en pause, sans retirer les fonctions du cabinet.
        AssistantStatusResponse status = (await client.GetFromJsonAsync("/api/v1/assistant/statut", ContractsJsonContext.Default.AssistantStatusResponse))!;
        status.Features.Should().Contain(AssistantFeature.RegulatoryQuestions);
        status.EngineResponding.Should().BeFalse();

        // Pendant la pause, la réponse se termine aussitôt (après l'éventuel avertissement sur le référentiel métier).
        IReadOnlyList<AssistantEvent> paused = await AskAsync(client);
        paused[^1].Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EnginePaused));
        paused.Should().NotContain(e => e is AssistantTextEvent || e is AssistantToolUsedEvent);
        engine.Calls.Should().Be(3, "pendant la pause, le moteur n'est plus appelé");
        IReadOnlyList<AssistantJournalEntry> journal = (await client.GetFromJsonAsync("/api/v1/assistant/journal", ContractsJsonContext.Default.IReadOnlyListAssistantJournalEntry))!;
        journal.Where(e => e.Kind == nameof(AiAuditKind.EngineFailed)).Select(e => e.Detail).Should()
            .Contain("moteur injoignable").And.Contain("moteur en pause après des échecs répétés");
    }

    [Fact]
    public async Task Un_moteur_qui_repond_est_dit_disponible()
    {
        using WebApplicationFactory<Program> enabled = Enabled(new EchoEngine());
        using HttpClient client = Client(enabled);

        AssistantStatusResponse status = (await client.GetFromJsonAsync("/api/v1/assistant/statut", ContractsJsonContext.Default.AssistantStatusResponse))!;

        status.Engine.Should().Be("test:echo");
        status.EngineResponding.Should().BeTrue();
    }

    private WebApplicationFactory<Program> Enabled(IAiEngine engine) => factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Ai:Enabled"] = "true",
            ["Lettra:Ai:Engine"] = nameof(AiEngineKind.ClaudeEu),
            ["Lettra:Ai:Features:0"] = nameof(AssistantFeature.RegulatoryQuestions),
        }));
        builder.ConfigureTestServices(services => services.AddKeyedSingleton(AiEngineKind.ClaudeEu, engine));
    });

    private static async Task<List<AssistantEvent>> AskAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/assistant", Question, ContractsJsonContext.Default.AskAssistantRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string[] lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. lines.Select(l => JsonSerializer.Deserialize(l, ContractsJsonContext.Default.AssistantEvent)!)];
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private sealed class BrokenEngine : IAiEngine
    {
        private int _calls;

        public int Calls => _calls;

        public string Name => "test:panne";

        public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            throw new HttpRequestException("503 Service Unavailable");
        }
    }

    private sealed class EchoEngine : IAiEngine
    {
        public string? LastQuestion { get; private set; }

        public string Name => "test:echo";

        public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken)
        {
            LastQuestion = string.Concat(request.Messages[0].Parts.OfType<TextPart>().Select(p => p.Text));
            return Task.FromResult(new EngineTurn([new TextPart("Le taux normal de 20 % s'applique.")], EngineStopReason.EndTurn));
        }
    }
}
