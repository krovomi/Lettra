using System.Text.Json;
using Lettra.Ai.Orchestration;
using Lettra.Ai.Tools.Tools;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Tests;

/// <summary>Les outils proposés au modèle n'ont aucun pouvoir : lecture ou brouillon, jamais une action humaine (ADR-0006).</summary>
public sealed class ToolRegistryTests
{
    [Theory]
    [InlineData("valider_devis")]
    [InlineData("emettre_facture")]
    [InlineData("envoyer_facture_pa")]
    [InlineData("supprimer_brouillon")]
    [InlineData("exporter_fec_complet")]
    [InlineData("payer_fournisseur")]
    [InlineData("signer_devis")]
    public void Refuse_un_outil_dont_le_nom_evoque_une_action_humaine(string name) =>
        FluentActions.Invoking(() => new ToolRegistry([new FakeTool(name, AssistantToolRisk.ReadOnly)]))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Refuse_un_pouvoir_hors_lecture_et_brouillon() =>
        FluentActions.Invoking(() => new ToolRegistry([new FakeTool("outil", (AssistantToolRisk)3)]))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Refuse_deux_outils_du_meme_nom() =>
        FluentActions.Invoking(() => new ToolRegistry([new FakeTool("outil", AssistantToolRisk.ReadOnly), new FakeTool("outil", AssistantToolRisk.ReadOnly)]))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Un_outil_n_est_propose_qu_avec_sa_permission_et_pour_ses_fonctions()
    {
        ToolRegistry registry = new([new FakeTool("lire_tiers", AssistantToolRisk.ReadOnly, Permission.TiersConsulter)]);
        AiSettings settings = new() { Enabled = true };

        registry.For(AssistantFeature.RegulatoryQuestions, new HashSet<Permission> { Permission.IaUtiliser }, settings).Should().BeEmpty();
        registry.For(AssistantFeature.RegulatoryQuestions, new HashSet<Permission> { Permission.IaUtiliser, Permission.TiersConsulter }, settings).Should().ContainSingle();
        registry.For(AssistantFeature.DraftFromText, new HashSet<Permission> { Permission.IaUtiliser, Permission.TiersConsulter }, settings).Should().BeEmpty();
    }

    [Fact]
    public async Task Les_outils_du_produit_lisent_ou_creent_un_brouillon_et_la_consultation_web_suit_le_reglage_du_cabinet()
    {
        await using AssistantHarness h = new(new ScriptedTurnsEngine(_ => ScriptedTurnsEngine.Say("ok")));
        await using AsyncServiceScope scope = h.Get<IServiceProvider>().CreateAsyncScope();
        ToolRegistry registry = scope.ServiceProvider.GetRequiredService<ToolRegistry>();

        registry.All.Select(t => t.Name).Should().BeEquivalentTo(
            "calculer", "chercher_article", "chercher_client", "consulter_site_officiel", "controler_document_ouvert",
            "creer_brouillon_devis", "lire_reference", "lister_references");

        // Un seul outil écrit, et seulement un brouillon, avec la permission de l'utilisateur et pour le point 1.
        IAiTool draft = registry.All.Single(t => t.Risk != AssistantToolRisk.ReadOnly);
        draft.Name.Should().Be(CreateQuoteDraftTool.ToolName);
        draft.Risk.Should().Be(AssistantToolRisk.CreatesDraft);
        draft.RequiredPermission.Should().Be(Permission.DevisCreer);
        draft.Features.Should().Equal(AssistantFeature.DraftFromText);
        registry.All.Single(t => t.Name == OfficialWebLookupTool.ToolName).ReceivesRealValues.Should().BeFalse();

        registry.For(AssistantFeature.RegulatoryQuestions, AssistantHarness.AllPermissions, new AiSettings { Enabled = true })
            .Should().NotContain(t => t.Name == OfficialWebLookupTool.ToolName);
        registry.For(AssistantFeature.RegulatoryQuestions, AssistantHarness.AllPermissions, new AiSettings { Enabled = true, OfficialWebLookup = true })
            .Should().Contain(t => t.Name == OfficialWebLookupTool.ToolName);
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
        OfficialWebLookupTool.Reject(url).Should().Be(reason);

    private sealed class FakeTool(string name, AssistantToolRisk risk, Permission? permission = null) : IAiTool
    {
        public string Name => name;

        public string Description => string.Empty;

        public JsonElement InputSchema => JsonSerializer.SerializeToElement(new { type = "object" });

        public AssistantToolRisk Risk => risk;

        public Permission? RequiredPermission => permission;

        public IReadOnlySet<AssistantFeature> Features { get; } = new HashSet<AssistantFeature> { AssistantFeature.RegulatoryQuestions };

        public bool ReceivesRealValues => true;

        public bool IsEnabledBy(AiSettings settings) => true;

        public Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
