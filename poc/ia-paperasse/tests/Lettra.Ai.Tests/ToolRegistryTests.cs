using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Orchestration;

namespace Lettra.Ai.Tests;

public sealed class ToolRegistryTests
{
    private sealed class FakeTool(string name, AiToolRisk risk) : IAiTool
    {
        public string Name => name;
        public string Description => "";
        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new { type = "object" });
        public AiToolRisk Risk => risk;
        public string RequiredPermission => AiPermissions.UseAssistant;
        public IReadOnlySet<AiFeature> Features => new HashSet<AiFeature> { AiFeature.RegulatoryQuestions };
        public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("valider_facture")]
    [InlineData("valider_devis")]
    [InlineData("emettre_facture")]
    [InlineData("envoyer_facture_pa")]
    [InlineData("supprimer_brouillon")]
    [InlineData("exporter_fec_complet")]
    public void Refuse_un_outil_dont_le_nom_evoque_une_action_humaine(string name) =>
        Assert.Throws<InvalidOperationException>(() => new ToolRegistry([new FakeTool(name, AiToolRisk.ReadOnly)]));

    [Fact]
    public void Refuse_un_pouvoir_hors_lecture_et_brouillon() =>
        Assert.Throws<InvalidOperationException>(() => new ToolRegistry([new FakeTool("outil", (AiToolRisk)2)]));

    [Fact]
    public void Verifier_facture_doit_rester_en_lecture_seule() =>
        Assert.Throws<InvalidOperationException>(() => new ToolRegistry([new FakeTool("verifier_facture", AiToolRisk.CreatesDraft)]));

    [Fact]
    public async Task Les_outils_du_produit_sont_tous_sans_pouvoir_et_proposes_selon_droits_et_fonction()
    {
        await using var h = new AssistantHarness(new ScriptedTurnsEngine(_ => ScriptedTurnsEngine.Say("ok")));
        var registry = h.Get<ToolRegistry>();

        Assert.All(registry.All, t => Assert.True(t.Risk is AiToolRisk.ReadOnly or AiToolRisk.CreatesDraft));
        Assert.Equal(["brouillon_devis"], registry.All.Where(t => t.Risk == AiToolRisk.CreatesDraft).Select(t => t.Name));

        var settings = AssistantHarness.Enabled();
        var readOnlyUser = new HashSet<string> { AiPermissions.UseAssistant, AiPermissions.ReadInvoices };
        Assert.DoesNotContain(registry.For(AiFeature.DraftFromText, readOnlyUser, settings), t => t.Name == "brouillon_devis");
        Assert.Contains(registry.For(AiFeature.DraftFromText, AssistantHarness.AllPermissions, settings), t => t.Name == "brouillon_devis");
        Assert.DoesNotContain(registry.For(AiFeature.ExplainCompliance, AssistantHarness.AllPermissions, settings), t => t.Name == "brouillon_devis");
        Assert.DoesNotContain(registry.For(AiFeature.RegulatoryQuestions, AssistantHarness.AllPermissions, settings), t => t.Name == "consulter_site_officiel");
        Assert.Contains(registry.For(AiFeature.RegulatoryQuestions, AssistantHarness.AllPermissions, AssistantHarness.Enabled(web: true)), t => t.Name == "consulter_site_officiel");
    }

    [Theory]
    [InlineData("https://bofip.impots.gouv.fr/bofip/1234-PGP.html", null)]
    [InlineData("https://www.impots.gouv.fr/professionnel/je-passe-la-facturation-electronique", null)]
    [InlineData("http://impots.gouv.fr/x", "seul https est accepté")]
    [InlineData("https://impots.gouv.fr.pirate.example/x", "domaine non autorisé : impots.gouv.fr.pirate.example")]
    [InlineData("https://pirate.example/collecte", "domaine non autorisé : pirate.example")]
    [InlineData("https://impots.gouv.fr/recherche?q=987654321", "paramètres d'URL interdits")]
    [InlineData("https://impots.gouv.fr/[CLIENT_1]", "chemin non autorisé")]
    [InlineData("https://user:pass@impots.gouv.fr/x", "port ou identifiants interdits")]
    public void La_consultation_web_n_accepte_que_les_pages_officielles_sans_parametres(string url, string? reason) =>
        Assert.Equal(reason, Lettra.Ai.Tools.Tools.OfficialWebLookupTool.Reject(url));
}
