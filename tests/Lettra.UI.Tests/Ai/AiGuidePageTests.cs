using System.Globalization;
using Bunit;
using Lettra.Application.Abstractions.Ai;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Ai;
using Lettra.UI.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Ai;

/// <summary>
/// Page « Bien utiliser l'IA de Lettra » : marche sans IA, dit l'état de l'IA dans le dossier (éteinte, active, en pause),
/// montre le registre des usages et le journal IA du dossier avec tri et filtres, en français et en anglais.
/// </summary>
[Collection("culture")]
public sealed class AiGuidePageTests : BunitContext
{
    private static readonly Guid Someone = Guid.Parse("0199e3a0-0000-7000-8000-0000000000a1");
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeAssistantGateway _gateway = new();

    public AiGuidePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        UseCulture("fr-FR");
    }

    private static void UseCulture(string name) => LettraCultures.Apply(CultureInfo.GetCultureInfo(name));

    private static AssistantJournalEntry Entry(int minutes, string kind, string detail, string? engine = "claude:claude-sonnet") =>
        new(Morning.AddMinutes(minutes), Someone, kind, detail, engine);

    [Fact]
    public void Sans_IA_la_page_explique_tout_et_dit_que_l_IA_est_eteinte()
    {
        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.Find("h1").TextContent.Should().Be("Bien utiliser l'IA de Lettra");
        page.Find("#ai-state").GetAttribute("data-state").Should().Be("Off");
        page.Find("#ai-state h2").TextContent.Should().Be("L'IA est éteinte");
        page.FindAll("#ai-guide-open").Should().BeEmpty();
        page.FindAll(".lt-ai-card").Select(c => c.Id).Should().Equal("ai-guide-does", "ai-guide-never", "ai-guide-mark", "ai-guide-off", "ai-guide-failure");
        page.FindAll(".lt-ai-card li").Should().HaveCount(19).And.OnlyContain(li => !string.IsNullOrWhiteSpace(li.TextContent) && !li.TextContent.StartsWith("AiGuide.", StringComparison.Ordinal));
        page.Find("#ai-guide-never").TextContent.Should().Contain("Valider, émettre ou envoyer un document")
            .And.Contain("Noter la solvabilité d'une personne");
        page.Find("#ai-guide-mark").TextContent.Should().Contain("« Texte rédigé par l'IA, à vérifier »");
        page.Find("#ai-journal-no-access").TextContent.Should().Contain("accès à l'assistant IA");
        Status(page, "draft").Should().Be("Fermé tant que Lettra ne sait pas masquer les noms tapés");
        Status(page, "explain").Should().Be("Non ouvert");
    }

    [Fact]
    public void IA_active_la_page_donne_le_moteur_les_fonctions_ouvertes_et_le_registre()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Status = new AssistantStatusResponse([AssistantFeature.ExplainCompliance, AssistantFeature.RegulatoryQuestions], "claude:claude-sonnet");

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.WaitForAssertion(() => page.Find("#ai-state").GetAttribute("data-state").Should().Be("On"));
        page.Find("#ai-state-engine").TextContent.Should().Be("claude:claude-sonnet");
        page.Find("#ai-state-features").TextContent.Should().Be("Pourquoi ? (conformité), Question TVA ou réglementaire");
        Status(page, "explain").Should().Be("Ouvert dans ce dossier");
        Status(page, "einvoicing").Should().Be("Non ouvert");
        Status(page, "external").Should().Be("Ouvert ou non par le cabinet, avec l'accord du DPO");
        Status(page, "speech").Should().Be("Selon le réglage de la dictée");
        page.Find("li[data-usage='speech']").TextContent.Should().Contain("Risque minimal");
        page.Find("li[data-usage='explain']").TextContent.Should().Contain("Risque limité");

        page.Find("#ai-guide-open").Click();
        AssistantPanelState state = Services.GetRequiredService<AssistantPanelState>();
        state.IsOpen.Should().BeTrue();
        state.Feature.Should().Be(AssistantFeature.RegulatoryQuestions);
    }

    [Fact]
    public void Un_moteur_en_pause_est_annonce()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Status = new AssistantStatusResponse([AssistantFeature.ExplainCompliance], "local:mistral-small", EngineResponding: false);

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.WaitForAssertion(() => page.Find("#ai-state").GetAttribute("data-state").Should().Be("Paused"));
        page.Find("#ai-state h2").TextContent.Should().Be("Le moteur d'IA ne répond pas");

        // Une réponse du moteur rétablit l'état sur la page aussi.
        page.InvokeAsync(() => Services.GetRequiredService<AssistantPanelState>().ReportEngine(responding: true));
        page.WaitForAssertion(() => page.Find("#ai-state").GetAttribute("data-state").Should().Be("On"));
    }

    [Fact]
    public void Le_journal_se_lit_avec_ses_evenements_traduits_et_se_trie_et_se_filtre()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Journal =
        [
            Entry(3, "EngineFailed", "délai dépassé"),
            Entry(2, "ToolCall", "calculer"),
            Entry(1, "EngineCall", "1 message", "local:mistral-small"),
            Entry(0, "Request", "Prorata de [MONTANT_1] sur 50 jours ?", null),
        ];

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.WaitForAssertion(() => page.FindAll("#ai-journal-table tbody tr").Should().HaveCount(4));
        Kinds(page).Should().Equal("Panne du moteur", "Outil utilisé", "Appel au moteur", "Demande");
        page.Find("#ai-journal-table tbody tr:last-child").TextContent.Should().Contain("—", because: "une demande n'a pas encore de moteur");
        page.Find("#ai-journal-count").TextContent.Should().Be("4 entrée(s) affichée(s) sur les 4 plus récentes");
        _gateway.JournalReads.Should().Be(1);

        page.Find("#ai-journal-sort-kind").Click();
        Kinds(page).Should().Equal("Appel au moteur", "Demande", "Outil utilisé", "Panne du moteur");

        page.Find("#ai-journal-kind").Change("EngineFailed");
        Kinds(page).Should().Equal("Panne du moteur");
        page.Find("#ai-journal-filters-reset").Click();

        page.Find("#ai-journal-search").Input("mistral");
        Kinds(page).Should().Equal("Appel au moteur");
        page.Find("#ai-journal-search").Input("zorglub");
        page.Find("#ai-journal-filtered-empty").Should().NotBeNull();
        page.Find("#ai-journal-empty-reset").Click();
        page.FindAll("#ai-journal-table tbody tr").Should().HaveCount(4);
    }

    [Fact]
    public void Un_journal_vide_le_dit()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.WaitForAssertion(() => page.Find("#ai-journal-empty").TextContent.Should().Contain("Le journal est vide"));
    }

    [Fact]
    public void Sans_droit_a_l_assistant_le_journal_n_est_pas_demande()
    {
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.StatusRefusal = ErrorCodes.Security.Forbidden;

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.WaitForElement("#ai-journal-no-access");
        page.Find("#ai-state").GetAttribute("data-state").Should().Be("Off");
        _gateway.JournalReads.Should().Be(0, because: "un refus attendu ne doit pas laisser d'erreur 403 dans la console du navigateur");
    }

    [Fact]
    public void La_page_est_traduite_en_anglais()
    {
        UseCulture("en-US");
        Services.AddSingleton<IAssistantGateway>(_gateway);
        _gateway.Status = new AssistantStatusResponse([AssistantFeature.ExplainCompliance], "local:mistral-small");
        _gateway.Journal = [Entry(0, "EngineFailed", "délai dépassé")];

        IRenderedComponent<AiGuidePage> page = Render<AiGuidePage>();

        page.Find("h1").TextContent.Should().Be("Using Lettra's AI well");
        page.WaitForAssertion(() => Kinds(page).Should().Equal("Engine failure"));
        page.Find("#ai-guide-mark").TextContent.Should().Contain("“Text written by AI, to be checked”");
        Status(page, "draft").Should().Be("Closed until Lettra can mask typed names");
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Chaque_usage_du_registre_est_ecrit_dans_les_deux_langues_et_chaque_fonction_y_figure(string culture)
    {
        UseCulture(culture);
        IStringLocalizer<SharedResource> l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        AiUsages.All.SelectMany(u => new[] { u.NameKey, u.RoleKey, u.RiskKey, u.StatusKey([]), u.StatusKey([AssistantFeature.ExplainCompliance]) })
            .Where(k => l[k].ResourceNotFound).Should().BeEmpty();
        AiUsages.All.Select(u => u.Id).Should().OnlyHaveUniqueItems();
        AiUsages.All.Select(u => u.Feature).OfType<AssistantFeature>().Should().BeEquivalentTo(Enum.GetValues<AssistantFeature>(),
            because: "une nouvelle fonction de l'assistant doit entrer au registre des usages de l'IA");
        Enum.GetNames<AiAuditKind>().Select(k => "AiGuide.Journal.Kind." + k).Where(k => l[k].ResourceNotFound).Should().BeEmpty();
    }

    [Fact]
    public void La_page_est_dans_le_menu_Pilotage() =>
        NavItems.All.Should().ContainSingle(i => i.Href == "ai").Which.GroupKey.Should().Be("Nav.Group.Settings");

    private static string Status(IRenderedComponent<AiGuidePage> page, string usage) =>
        page.Find($"li[data-usage='{usage}'] .is-status").TextContent;

    private static IEnumerable<string> Kinds(IRenderedComponent<AiGuidePage> page) =>
        page.FindAll("#ai-journal-table tbody .lt-ai-kind").Select(e => e.TextContent);
}
