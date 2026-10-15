using System.Net;
using System.Net.Http.Json;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Réglages IA du cabinet dans la base Catalog : l'administrateur du cabinet les change, l'assistant les suit aussitôt.</summary>
public sealed class AiSettingsApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Settings(Guid cabinet) => new($"/api/v1/cabinets/{cabinet}/assistant/reglages", UriKind.Relative);

    private static SaveAiSettingsRequest EnableRegulatoryQuestions(long? version) => new()
    {
        Enabled = true,
        Engine = AssistantEngine.ClaudeEu,
        Features = [AssistantFeature.RegulatoryQuestions],
        ExcludedDossiers = [Guid.Parse("0199e3a0-0000-7000-8000-0000000000d9")],
        Version = version,
    };

    [Fact]
    public async Task L_administrateur_active_l_IA_dans_la_base_Catalog_et_l_assistant_repond_aussitot()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        // Rien d'enregistré : ce sont les réglages du déploiement, IA désactivée.
        AiSettingsResponse initial = (await admin.GetFromJsonAsync(Settings(LettraApiFactory.CabinetId), ContractsJsonContext.Default.AiSettingsResponse))!;
        initial.Should().BeEquivalentTo(new { Enabled = false, Engine = AssistantEngine.None, Source = AiSettingsSource.Configuration, Version = (long?)null });

        HttpResponseMessage saved = await admin.PutAsJsonAsync(
            Settings(LettraApiFactory.CabinetId), EnableRegulatoryQuestions(null), ContractsJsonContext.Default.SaveAiSettingsRequest);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        AiSettingsResponse stored = (await saved.Content.ReadFromJsonAsync(ContractsJsonContext.Default.AiSettingsResponse))!;
        stored.Should().BeEquivalentTo(new
        {
            Enabled = true,
            Engine = AssistantEngine.ClaudeEu,
            Features = new[] { AssistantFeature.RegulatoryQuestions },
            Source = AiSettingsSource.Catalog,
            Version = (long?)1,
        });
        stored.UpdatedAt.Should().NotBeNull();

        // Un second administrateur qui enregistre sur la version qu'il avait lue (aucune) est refusé.
        HttpResponseMessage stale = await admin.PutAsJsonAsync(
            Settings(LettraApiFactory.CabinetId), EnableRegulatoryQuestions(null), ContractsJsonContext.Default.SaveAiSettingsRequest);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Ai.SettingsConflict);

        // L'assistant suit la base Catalog, sans rien dans la configuration.
        using WebApplicationFactory<Program> withEngine = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddKeyedSingleton<IAiEngine>(AiEngineKind.ClaudeEu, new AnswerEngine())));
        using HttpClient manager = withEngine.CreateClient();
        manager.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        using HttpResponseMessage answer = await manager.PostAsJsonAsync(
            "/api/v1/assistant",
            new AskAssistantRequest { Feature = AssistantFeature.RegulatoryQuestions, Question = "Taux de TVA d'une prestation de conseil ?" },
            ContractsJsonContext.Default.AskAssistantRequest);
        answer.StatusCode.Should().Be(HttpStatusCode.OK);

        // Puis l'administrateur la coupe : refus immédiat.
        (await admin.PutAsJsonAsync(
            Settings(LettraApiFactory.CabinetId),
            new SaveAiSettingsRequest { Enabled = false, Version = 1 },
            ContractsJsonContext.Default.SaveAiSettingsRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage refused = await manager.PostAsJsonAsync(
            "/api/v1/assistant",
            new AskAssistantRequest { Feature = AssistantFeature.RegulatoryQuestions, Question = "Et maintenant ?" },
            ContractsJsonContext.Default.AskAssistantRequest);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Ai.Disabled);
    }

    [Fact]
    public async Task Activer_sans_moteur_est_refuse_avec_son_code()
    {
        using HttpClient admin = factory.ClientFor(LettraApiFactory.AdministrateurCabinetId);

        HttpResponseMessage response = await admin.PutAsJsonAsync(
            Settings(LettraApiFactory.CabinetId),
            new SaveAiSettingsRequest { Enabled = true, Engine = AssistantEngine.None },
            ContractsJsonContext.Default.SaveAiSettingsRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Ai.EngineRequired);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("collaborateur")]
    [InlineData("autre-cabinet")]
    public async Task Seul_l_administrateur_de_ce_cabinet_lit_ou_change_les_reglages(string who)
    {
        Guid user = who switch
        {
            "manager" => LettraApiFactory.ManagerId,
            "collaborateur" => LettraApiFactory.CollaborateurId,
            _ => LettraApiFactory.AutreCabinetAdminId,
        };
        using HttpClient client = factory.ClientFor(user);

        (await client.GetAsync(Settings(LettraApiFactory.CabinetId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Settings(LettraApiFactory.CabinetId), EnableRegulatoryQuestions(null), ContractsJsonContext.Default.SaveAiSettingsRequest))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed class AnswerEngine : IAiEngine
    {
        public string Name => "test:reponse";

        public Task<EngineTurn> CompleteAsync(EngineRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new EngineTurn([new TextPart("20 %.")], EngineStopReason.EndTurn));
    }
}
