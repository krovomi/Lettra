using System.Globalization;
using System.Runtime.CompilerServices;
using Bunit;
using Bunit.Rendering;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Assistant;
using Lettra.UI.Shared.Components.Billing;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests;

/// <summary>
/// Panneau assistant (points 1 et 3) : absent tant que le cabinet n'a pas activé l'IA ; « Pourquoi ? » sur la checklist
/// envoie le document ouvert ; un brouillon s'ouvre dans son écran ; aucun bouton ne valide ni n'émet.
/// </summary>
[Collection("culture")]
public sealed class AssistantPanelTests : BunitContext
{
    private static readonly Guid DocumentId = Guid.Parse("0199e3a0-0000-7000-8000-00000000d001");

    private static readonly ComplianceReportResponse Report = new(true,
    [
        new("Compliance.DueDateMissing", ComplianceSeverityDto.Error, "CGI art. 242 nonies A"),
        new("Compliance.SellerNameMissing", ComplianceSeverityDto.Ok, null),
    ]);

    private readonly FakeAssistantGateway _gateway = new();

    public AssistantPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<ContainerFragment> RenderScreen() => Render(builder =>
    {
        builder.OpenComponent<ComplianceChecklist>(0);
        builder.AddAttribute(1, nameof(ComplianceChecklist.Report), Report);
        builder.AddAttribute(2, nameof(ComplianceChecklist.DocumentId), (Guid?)DocumentId);
        builder.CloseComponent();
        builder.OpenComponent<AssistantPanel>(3);
        builder.CloseComponent();
    });

    [Fact]
    public void Sans_gateway_ni_reglage_du_cabinet_rien_ne_s_affiche()
    {
        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        Services.GetRequiredService<AssistantPanelState>().Open(AssistantFeature.RegulatoryQuestions);

        screen.FindAll(".lettra-why").Should().BeEmpty();
        screen.FindAll(".lettra-assistant").Should().BeEmpty();
    }

    [Fact]
    public void IA_desactivee_par_le_cabinet_rien_ne_s_affiche()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Status = new AssistantStatusResponse([], null);

        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        Services.GetRequiredService<AssistantPanelState>().Open(AssistantFeature.RegulatoryQuestions);

        screen.FindAll(".lettra-why").Should().BeEmpty();
        screen.FindAll(".lettra-assistant").Should().BeEmpty();
    }

    [Fact]
    public void Pourquoi_ouvre_le_panneau_sur_le_constat_et_le_document_ouvert()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Answer =
        [
            new AssistantToolUsedEvent("controler_document_ouvert", AssistantToolRisk.ReadOnly, true),
            new AssistantRuleResultEvent("controler_document_ouvert", "{\"bloque_la_validation\":true}"),
            new AssistantTextEvent("La date d'échéance manque : ajoutez-la."),
            new AssistantCompletedEvent("local:mistral-small", 1, null, null, "f2e15c4"),
        ];

        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        screen.FindAll(".lettra-why").Should().ContainSingle(because: "seul le constat bloquant a un « Pourquoi ? »");
        screen.Find(".lettra-why").Click();

        screen.WaitForAssertion(() => screen.FindAll(".lettra-assistant-events li").Should().HaveCount(4));
        AskAssistantRequest request = _gateway.Requests.Single();
        request.Feature.Should().Be(AssistantFeature.ExplainCompliance);
        request.OpenDocumentId.Should().Be(DocumentId);
        request.Question.Should().Contain("Compliance.DueDateMissing");
        screen.Find(".is-step").TextContent.Should().Be("Étape : contrôle de conformité de la pièce");
        screen.Find(".is-suggestion .lettra-assistant-label").TextContent.Should().Be("Suggestion de l'IA, à vérifier");
        screen.Find(".is-completed").TextContent.Should().Contain("local:mistral-small");
        screen.FindAll("button").Select(b => b.TextContent).Should().NotContain(t => t.Contains("Valider") || t.Contains("Émettre"));
    }

    [Fact]
    public async Task Un_brouillon_propose_s_ouvre_dans_l_ecran_du_devis_et_n_envoie_aucun_document()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Answer =
        [
            new AssistantToolUsedEvent("creer_brouillon_devis", AssistantToolRisk.CreatesDraft, true),
            new AssistantDraftProposedEvent(DocumentId, "quote", "Dupont SARL · 2 ligne(s) · 2850.00 € HT · 3420.00 € TTC"),
            new AssistantTextEvent("Le brouillon est prêt."),
        ];
        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        AssistantPanelState state = Services.GetRequiredService<AssistantPanelState>();
        screen.WaitForAssertion(() => state.IsAvailable.Should().BeTrue());
        state.Open(AssistantFeature.DraftFromText, DocumentId);

        screen.WaitForElement("#assistant-question").Input("Devis pour Dupont : 3 jours de conseil à 650 €");
        screen.Find("form").Submit();

        // Le flux re-rend le panneau à chaque événement : trouver et cliquer le bouton dans le même passage du moteur de rendu.
        screen.WaitForAssertion(() => screen.FindAll(".is-suggestion").Should().ContainSingle(e => e.TextContent.Contains("Le brouillon est prêt.")));
        await screen.InvokeAsync(() => screen.Find(".assistant-open-draft").Click());
        _gateway.Requests.Single().Should().BeEquivalentTo(new AskAssistantRequest
        {
            Feature = AssistantFeature.DraftFromText,
            Question = "Devis pour Dupont : 3 jours de conseil à 650 €",
            OpenDocumentId = null,
        });
        screen.Find(".is-draft").TextContent.Should().Contain("Brouillon de devis créé, à relire puis valider vous-même");
        screen.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/quotes/{DocumentId}"));
    }

    [Fact]
    public void Sans_brouillon_de_devis_le_panneau_explique_pourquoi_sans_jargon()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Status = new AssistantStatusResponse([AssistantFeature.ExplainCompliance, AssistantFeature.RegulatoryQuestions], "local:mistral-small");
        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        AssistantPanelState state = Services.GetRequiredService<AssistantPanelState>();
        screen.WaitForAssertion(() => state.IsAvailable.Should().BeTrue());

        state.Open(AssistantFeature.RegulatoryQuestions);

        screen.WaitForElement("#assistant-draft-blocked").TextContent
            .Should().Be("Le brouillon de devis à partir d'un texte libre arrivera quand Lettra saura masquer les noms tapés dans le texte.");
        screen.FindAll("#assistant-feature option").Select(o => o.TextContent).Should().NotContain("Brouillon de devis");
    }

    [Fact]
    public void Un_brouillon_de_devis_ferme_par_le_serveur_est_explique()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Answer = [new AssistantUnavailableEvent(ErrorCodes.Ai.FeatureBlocked)];
        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        screen.WaitForElement(".lettra-why").Click();

        screen.WaitForAssertion(() => screen.Find(".lettra-assistant-events .is-error").TextContent
            .Should().StartWith("Le brouillon de devis à partir d'un texte libre n'est pas encore disponible"));
        screen.FindAll("#assistant-draft-blocked").Should().BeEmpty(because: "la passerelle de test propose encore le point 1");
    }

    [Fact]
    public void Un_refus_du_serveur_est_traduit()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Answer = [new AssistantUnavailableEvent(ErrorCodes.Ai.Disabled)];
        IRenderedComponent<ContainerFragment> screen = RenderScreen();
        screen.WaitForElement(".lettra-why").Click();

        screen.WaitForAssertion(() => screen.Find(".lettra-assistant-events .is-error").TextContent
            .Should().Be("L'assistant IA est désactivé pour ce cabinet. L'administrateur du cabinet peut l'activer."));
    }

    private sealed class FakeAssistantGateway : IAssistantGateway
    {
        public AssistantStatusResponse Status { get; set; } = new(
            [AssistantFeature.DraftFromText, AssistantFeature.ExplainCompliance, AssistantFeature.RegulatoryQuestions], "local:mistral-small");

        public IReadOnlyList<AssistantEvent> Answer { get; set; } = [];

        public List<AskAssistantRequest> Requests { get; } = [];

        public Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<AssistantStatusResponse>.Success(Status));

        public async IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            foreach (AssistantEvent item in Answer)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
