using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Poc.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lettra.Ai.Tests;

/// <summary>Parcours de bout en bout sur l'API de démonstration du POC.</summary>
public sealed class PocApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient Client(string permissions = "Ia.Utiliser,Factures.Lire,Devis.CreerBrouillon")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "00000000-0000-4000-8000-0000000000a1");
        client.DefaultRequestHeaders.Add("X-Permissions", permissions);
        return client;
    }

    [Fact]
    public async Task Le_controle_de_conformite_fonctionne_sans_IA()
    {
        var response = await Client().GetFromJsonAsync<JsonElement>(
            $"/api/dossiers/{SampleDossiers.WebCraftId}/factures/{SampleDossiers.InvoiceIncompleteId}/conformite");
        Assert.Equal("NON CONFORME", response.GetProperty("status").GetString());
        Assert.True(response.GetProperty("blocksValidation").GetBoolean());
    }

    [Fact]
    public async Task Parcours_complet_activation_question_brouillon_validation_humaine_journal()
    {
        var client = Client();
        var cabinet = SampleDossiers.CabinetId;
        var dossier = SampleDossiers.WebCraftId;

        // L'IA est désactivée tant que le cabinet ne l'active pas.
        var before = await client.PostAsJsonAsync($"/api/dossiers/{dossier}/assistant", new { feature = "DraftFromText", question = "Devis Dupont, 3 h de conseil à 120 € HT" });
        Assert.Equal("indisponible", (await before.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("type").GetString());

        var put = await client.PutAsJsonAsync($"/api/cabinets/{cabinet}/ia/reglages",
            new { enabled = true, engine = "Stub", features = new[] { "DraftFromText", "ExplainCompliance" }, officialWebLookup = false });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var answer = await client.PostAsJsonAsync($"/api/dossiers/{dossier}/assistant", new { feature = "DraftFromText", question = "Devis Dupont, 3 h de conseil à 120 € HT" });
        var events = (await answer.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var draft = events.Single(e => e.GetProperty("type").GetString() == "brouillon").GetProperty("draft");
        Assert.Contains(events, e => e.GetProperty("type").GetString() == "texte_ia" && e.GetProperty("text").GetString()!.Contains("432,00 EUR"));

        var validated = await client.PostAsync($"/api/dossiers/{dossier}/devis/{draft.GetProperty("draftId").GetString()}/valider", null);
        Assert.Equal("Valide", (await validated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var journal = await client.GetFromJsonAsync<JsonElement>($"/api/dossiers/{dossier}/ia/journal");
        Assert.Contains(journal.EnumerateArray(), e => e.GetProperty("kind").GetString() == "Draft" && e.GetProperty("origin").GetString() == "AssistantIA");
    }

    [Fact]
    public async Task L_ecran_a_propos_reprend_la_notice_MIT_de_Paperasse()
    {
        var about = await Client().GetFromJsonAsync<JsonElement>("/api/a-propos");
        Assert.Contains("licence MIT", about.GetProperty("notice").GetString());
        Assert.StartsWith("f2e15c4", about.GetProperty("skillPack").GetProperty("commit").GetString());
    }
}
