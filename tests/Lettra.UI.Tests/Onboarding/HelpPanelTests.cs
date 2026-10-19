using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Palette;
using Lettra.UI.Shared.Layout;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Onboarding;
using Lettra.UI.Shared.Pages;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lettra.UI.Tests.Onboarding;

/// <summary>
/// Aide « Besoin d'aide ? » : questions fréquentes sans IA, celles de l'écran en tête, question en langage courant,
/// tutoriel, recherche générale, et l'assistant seulement s'il est activé.
/// </summary>
[Collection("culture")]
public sealed class HelpPanelTests : BunitContext
{
    public HelpPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule(UiModule.Path);
        JSInterop.SetupModule(TourModule.Path);
        Services.AddLettraUi();
        Services.AddCabinetFakes();
        Services.AddSingleton<IInvoicesGateway>(new FakeInvoicesGateway());
        Services.AddSingleton<IQuotesGateway>(new FakeQuotesGateway());
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        Services.AddSingleton<TimeProvider>(new ManualClock(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<MainLayout> Coque() => Render<MainLayout>(p => p.Add(l => l.Body, b =>
    {
        b.OpenComponent<Home>(0);
        b.CloseComponent();
    }));

    private static string[] Questions(IEnumerable<IElement> faq) => [.. faq.Select(d => d.QuerySelector("summary")!.TextContent.Trim())];

    [Fact]
    public void Le_bouton_aide_ouvre_le_panneau_qu_echap_referme()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.FindAll("#help-panel").Should().BeEmpty();

        coque.Find("#tour-help").Click();

        IElement panneau = coque.Find("#help-panel");
        panneau.GetAttribute("role").Should().Be("dialog");
        panneau.GetAttribute("aria-modal").Should().Be("true");
        coque.Find("#help-title").TextContent.Trim().Should().Be("Besoin d'aide\u00A0?");
        coque.Find("#tour-help").GetAttribute("aria-expanded").Should().Be("true");

        panneau.KeyDown("Escape");
        coque.FindAll("#help-panel").Should().BeEmpty();

        coque.Find("#nav-help").Click();
        coque.FindAll("#help-panel").Should().ContainSingle();
        coque.Find("#help-close").Click();
        coque.FindAll("#help-panel").Should().BeEmpty();
    }

    [Fact]
    public void Les_questions_de_l_ecran_affiche_passent_en_tete()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("invoices/42");
        IRenderedComponent<MainLayout> coque = Coque();

        coque.Find("#tour-help").Click();

        Questions(coque.FindAll("section[aria-labelledby=help-here-title] details")).Should().Equal(
            "Comment corriger ou annuler une facture déjà validée\u00A0?", "Comment enregistrer un paiement reçu\u00A0?", "Puis-je dicter au lieu de taper\u00A0?");
        coque.FindAll("section[aria-labelledby=help-faq-title] details").Should().HaveCount(HelpCatalog.All.Count - 3);
        coque.Find("#help-tour-start").TextContent.Should().Contain("Tutoriel de cet écran");
    }

    [Theory]
    [InlineData("je veux annuler ma facture", "fix-invoice")]
    [InlineData("Comment faire payer un client en retard", "reminder")]
    [InlineData("tva", "vat")]
    [InlineData("travailler sans internet", "offline")]
    [InlineData("ajouter un collègue", "team")]
    [InlineData("abonnement mensuel", "recurring")]
    public void Une_question_en_langage_courant_trouve_sa_reponse(string question, string attendue)
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();

        coque.Find("#help-query").Input(question);

        coque.Find("section[aria-labelledby=help-answers-title] details").Id.Should().Be($"help-faq-{attendue}");
        coque.Find("#help-results-status").TextContent.Should().EndWith("réponse(s)");
    }

    [Fact]
    public void Une_question_precise_ne_liste_pas_tout_ce_qui_parle_de_facture()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();

        coque.Find("#help-query").Input("je veux annuler ma facture");

        coque.FindAll("section[aria-labelledby=help-answers-title] details").Select(d => d.Id).Should().Equal("help-faq-fix-invoice");
        coque.Find("#help-faq-fix-invoice").HasAttribute("open").Should().BeTrue("une seule réponse se montre dépliée");
    }

    [Fact]
    public void Entree_deplie_la_meilleure_reponse_et_son_lien_mene_a_l_ecran()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();
        coque.Find("#help-query").Input("enregistrer un paiement");

        coque.Find(".lt-help-ask").Submit();

        IElement reponse = coque.Find("#help-faq-payment");
        reponse.HasAttribute("open").Should().BeTrue();
        IElement lien = reponse.QuerySelector(".lt-help-go")!;
        lien.TextContent.Trim().Should().Be("Ouvrir\u00A0: Saisir un encaissement");
        lien.GetAttribute("href").Should().Be("payments/new");
        lien.Click();
        coque.FindAll("#help-panel").Should().BeEmpty();
    }

    [Fact]
    public void Sans_reponse_et_sans_ia_l_aide_propose_d_autres_pistes()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();

        coque.Find("#help-query").Input("zorglub");

        coque.Find("#help-empty").TextContent.Should().Contain("cherchez partout avec Ctrl+K");
        coque.FindAll("#help-assistant").Should().BeEmpty("l'assistant est désactivé par défaut");
    }

    [Fact]
    public async Task Chercher_partout_ouvre_la_palette_avec_la_question()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();
        coque.Find("#help-query").Input("Dupont");

        await coque.Find("#help-palette").ClickAsync(new());

        coque.FindAll("#help-panel").Should().BeEmpty();
        coque.WaitForAssertion(() => coque.Find("#palette-input").GetAttribute("value").Should().Be("Dupont"));
    }

    [Fact]
    public void Le_tutoriel_de_l_ecran_se_relance_depuis_l_aide()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();

        coque.Find("#help-tour-start").Click();

        coque.FindAll("#help-panel").Should().BeEmpty();
        coque.Find("#tour-title").TextContent.Should().Be("Bienvenue dans Lettra");
    }

    [Fact]
    public void Revoir_tous_les_tutoriels_passe_par_le_toaster()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        coque.Find("#tour-help").Click();

        coque.Find("#help-tours-reset").Click();

        coque.WaitForAssertion(() => Services.GetRequiredService<ToastService>().Visible.Should().ContainSingle()
            .Which.Should().Be(new Toast(1, ToastKind.Success, "Les tutoriels seront rejoués à votre prochaine visite de chaque écran.")));
    }

    [Fact]
    public async Task Avec_l_assistant_active_la_question_lui_est_passee()
    {
        FakeAssistantGateway gateway = new();
        Services.AddSingleton<IAssistantGateway>(gateway);
        IRenderedComponent<MainLayout> coque = Coque();
        AssistantPanelState assistant = Services.GetRequiredService<AssistantPanelState>();
        await coque.InvokeAsync(() => assistant.LoadAsync());
        coque.Find("#tour-help").Click();
        coque.Find("#help-query").Input("zorglub");
        coque.Find("#help-empty").TextContent.Should().Contain("demandez à l'assistant");

        coque.Find("#help-assistant").Click();

        coque.FindAll("#help-panel").Should().BeEmpty();
        assistant.IsOpen.Should().BeTrue();
        assistant.Feature.Should().Be(AssistantFeature.RegulatoryQuestions);
        coque.WaitForAssertion(() => gateway.Questions.Should().Equal("zorglub"));
    }

    // ---------- palette Ctrl+K ----------

    [Fact]
    public async Task La_palette_propose_les_questions_de_l_aide_et_les_ouvre_depliees()
    {
        IRenderedComponent<MainLayout> coque = Coque();
        HelpPaletteProvider palette = Services.GetServices<ICommandPaletteProvider>().OfType<HelpPaletteProvider>().Single();

        IReadOnlyList<PaletteItem> items = await palette.SearchAsync("corriger facture", CancellationToken.None);
        (await palette.SearchAsync("avoir 0042", CancellationToken.None)).Should().BeEmpty("une pièce se cherche, pas une question");
        (await palette.SearchAsync("aide", CancellationToken.None)).Select(i => i.Label).Should().Contain("Aide et questions fréquentes");

        items[0].Label.Should().Be("Comment corriger ou annuler une facture déjà validée\u00A0?");
        await coque.InvokeAsync(items[0].Run);
        coque.WaitForAssertion(() => coque.Find("#help-faq-fix-invoice").HasAttribute("open").Should().BeTrue());
    }

    // ---------- contenu ----------

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Chaque_question_et_chaque_mot_cle_du_menu_est_ecrit_dans_les_deux_langues(string culture)
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo(culture));
        IStringLocalizer<SharedResource> l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        string[] cles =
        [
            .. HelpCatalog.All.SelectMany(e => new[] { e.QuestionKey, e.AnswerKey, e.KeywordsKey }),
            .. NavItems.All.Select(i => i.KeywordsKey),
        ];

        cles.Where(k => l[k].ResourceNotFound).Should().BeEmpty();
        HelpCatalog.All.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Chaque_lien_de_l_aide_mene_a_un_ecran_existant()
    {
        HashSet<string> routes =
        [
            .. typeof(MainLayout).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
                .Select(r => r.Template.Trim('/')),
        ];

        HelpCatalog.All.Where(e => e.LinkHref is not null).Should().OnlyContain(e => routes.Contains(e.LinkHref!) && e.LinkLabelKey != null);
    }

    private sealed class FakeAssistantGateway : IAssistantGateway
    {
        public List<string?> Questions { get; } = [];

        public Task<OperationResult<AssistantStatusResponse>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult<AssistantStatusResponse>.Success(new AssistantStatusResponse([AssistantFeature.RegulatoryQuestions], "local:mistral-small")));

        public async IAsyncEnumerable<AssistantEvent> AskAsync(AskAssistantRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Questions.Add(request.Question);
            await Task.Yield();
            yield break;
        }
    }
}
