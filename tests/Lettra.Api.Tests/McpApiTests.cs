using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Lettra.Api.Tests;

/// <summary>
/// Lettra dans l'assistant IA du poste, par l'API réelle (mode 2, SQLite) : le cabinet ouvre ce point seul, sans moteur ;
/// chaque outil répond avec les droits de l'utilisateur, aucun nom de client ne sort en clair et tout va au journal IA.
/// </summary>
public sealed class McpApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private const string Endpoint = "/api/v1/assistant/mcp";
    private const string SessionHeader = "Mcp-Session-Id";

    private WebApplicationFactory<Program> Enabled() => factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lettra:Ai:Enabled"] = "true",
            ["Lettra:Ai:Features:0"] = nameof(AssistantFeature.ExternalAssistant),
        })));

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static async Task<(HttpStatusCode Status, JsonNode? Body, string? Session)> PostAsync(HttpClient client, string? session, string json)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Endpoint) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (session is not null)
        {
            request.Headers.Add(SessionHeader, session);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        string? id = response.Headers.TryGetValues(SessionHeader, out IEnumerable<string>? values) ? values.Single() : null;
        return (response.StatusCode, body.Length == 0 ? null : JsonNode.Parse(body), id);
    }

    private static async Task<string> CallAsync(HttpClient client, string session, string tool, string arguments = "{}")
    {
        (HttpStatusCode status, JsonNode? body, _) = await PostAsync(
            client, session, $$$"""{"jsonrpc":"2.0","id":"{{{tool}}}","method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{arguments}}}}}""");
        status.Should().Be(HttpStatusCode.OK);
        body!["result"]!["isError"]!.GetValue<bool>().Should().BeFalse(body.ToJsonString());
        return body["result"]!["content"]![0]!["text"]!.GetValue<string>();
    }

    [Fact]
    public async Task L_assistant_du_poste_lit_le_dossier_sans_moteur_et_sans_nom_en_clair()
    {
        using WebApplicationFactory<Program> enabled = Enabled();
        using HttpClient client = InDossier(enabled.CreateClient());
        await ExportApiTests.ValidatedInvoiceAsync(client);

        (HttpStatusCode status, JsonNode? init, string? session) = await PostAsync(
            client, null, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"test"}}}""");
        (HttpStatusCode notified, _, _) = await PostAsync(client, session, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        (_, JsonNode? list, _) = await PostAsync(client, session, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        status.Should().Be(HttpStatusCode.OK);
        session.Should().NotBeNullOrEmpty();
        init!["result"]!["serverInfo"]!["name"]!.GetValue<string>().Should().Be("lettra");
        notified.Should().Be(HttpStatusCode.Accepted);
        list!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).Should().BeEquivalentTo(
            "meteo_des_paiements", "factures_en_retard", "chiffre_d_affaires_du_mois", "dossier_a_un_instant", "simuler_et_si",
            "fiche_d_appel", "calculer", "lire_reference", "lister_references");

        string month = LegalCalendar.DateOf(DateTimeOffset.UtcNow).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        string[] answers =
        [
            await CallAsync(client, session!, "meteo_des_paiements"),
            await CallAsync(client, session!, "factures_en_retard"),
            await CallAsync(client, session!, "chiffre_d_affaires_du_mois", $$"""{"mois":"{{month}}"}"""),
            await CallAsync(client, session!, "dossier_a_un_instant", $$"""{"instant":"{{DateTimeOffset.UtcNow:O}}"}"""),
            await CallAsync(client, session!, "simuler_et_si", """{"extraDelayDays":15,"vat":"Quarterly"}"""),
        ];
        string customer = JsonNode.Parse(answers[0])!["Customers"]![0]!["CustomerId"]!.GetValue<string>();
        string callSheet = await CallAsync(client, session!, "fiche_d_appel", $$"""{"client":"{{customer}}"}""");
        answers = [.. answers, callSheet];
        callSheet.Should().Contain("[CLIENT_");

        string revenue = answers[2];
        revenue.Should().Contain("[CLIENT_", "la facture validée du mois porte son client, pseudonymisé");
        answers.Should().OnlyContain(a => !a.Contains("Boulangerie Martin", StringComparison.Ordinal));
        IReadOnlyList<AssistantJournalEntry> journal = (await client.GetFromJsonAsync("/api/v1/assistant/journal", ContractsJsonContext.Default.IReadOnlyListAssistantJournalEntry))!;
        journal.Where(e => e.Kind == nameof(AiAuditKind.ToolCall)).Should().HaveCountGreaterThanOrEqualTo(5);
        journal.Should().Contain(e => e.Kind == nameof(AiAuditKind.Request));

        using HttpRequestMessage end = new(HttpMethod.Delete, Endpoint);
        end.Headers.Add(SessionHeader, session);
        (await client.SendAsync(end)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync(client, session, """{"jsonrpc":"2.0","id":3,"method":"ping"}""")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_parametre_mal_forme_revient_en_erreur_d_outil()
    {
        using WebApplicationFactory<Program> enabled = Enabled();
        using HttpClient client = InDossier(enabled.CreateClient());
        (_, _, string? session) = await PostAsync(client, null, """{"jsonrpc":"2.0","id":1,"method":"initialize"}""");

        foreach ((string tool, string arguments) in new[]
        {
            ("chiffre_d_affaires_du_mois", """{"mois":"octobre"}"""),
            ("dossier_a_un_instant", """{"instant":"hier"}"""),
            ("simuler_et_si", """{"invoice":{"amountExclTax":100,"vatRatePercent":20,"issueDate":"demain","paymentTermDays":30}}"""),
            ("simuler_et_si", """{"extraDelayDays":500}"""),
            ("fiche_d_appel", """{"client":"Boulangerie"}"""),
        })
        {
            (HttpStatusCode status, JsonNode? body, _) = await PostAsync(
                client, session, $$$"""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{arguments}}}}}""");

            status.Should().Be(HttpStatusCode.OK);
            body!["result"]!["isError"]!.GetValue<bool>().Should().BeTrue(tool);
        }
    }

    [Fact]
    public async Task Sans_activation_le_point_est_refuse()
    {
        using HttpClient client = InDossier(factory.CreateClient());

        (HttpStatusCode status, JsonNode? body, _) = await PostAsync(client, null, """{"jsonrpc":"2.0","id":1,"method":"initialize"}""");

        status.Should().Be(HttpStatusCode.Forbidden);
        body!["code"]!.GetValue<string>().Should().Be(ErrorCodes.Ai.Disabled);
    }

    [Fact]
    public async Task Un_corps_qui_n_est_pas_du_JSON_est_refuse()
    {
        using WebApplicationFactory<Program> enabled = Enabled();
        using HttpClient client = InDossier(enabled.CreateClient());

        using HttpResponseMessage text = await client.PostAsync(Endpoint, new StringContent("ping", Encoding.UTF8, "text/plain"));
        (HttpStatusCode unreadable, JsonNode? body, _) = await PostAsync(client, null, "{pas du json");
        using HttpRequestMessage unknown = new(HttpMethod.Delete, Endpoint);
        unknown.Headers.Add(SessionHeader, "inconnue");

        text.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        unreadable.Should().Be(HttpStatusCode.BadRequest);
        body!["error"]!["code"]!.GetValue<int>().Should().Be(-32700);
        (await client.SendAsync(unknown)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
