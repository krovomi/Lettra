using System.Globalization;
using Bunit;
using Lettra.Contracts.Common;
using Lettra.Contracts.Decisions;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Decisions;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Decisions;

/// <summary>
/// « Décisions à prendre » : les plus urgentes d'abord, des phrases remplies dans la langue de l'écran, l'action préparée
/// en un clic, « Plus tard » et « Ignorer » qui se défont, le cadran que seul un manager règle.
/// </summary>
[Collection("culture")]
public sealed class DecisionsPageTests : BunitContext
{
    private readonly FakeDecisionsGateway _decisions = new();

    public DecisionsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddSingleton<IDecisionsGateway>(_decisions);
        Services.AddSingleton<TimeProvider>(new ManualClock(FakeDecisionsGateway.Now));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private IRenderedComponent<DecisionsPage> Open()
    {
        IRenderedComponent<DecisionsPage> page = Render<DecisionsPage>();
        page.WaitForState(() => page.FindAll(".lt-skeleton").Count == 0);
        return page;
    }

    private static string[] Titles(IRenderedComponent<DecisionsPage> page) =>
        [.. page.FindAll("#decisions-list .lt-decision-title").Select(h => h.TextContent.Trim())];

    private DecisionRow Reminder() => _decisions.Add(
        "relance.due",
        new() { ["invoice"] = "F-1", ["customer"] = "Durand SARL", ["days"] = "20", ["level"] = "@ReminderLevel.Friendly" },
        canAccept: true, impact: 1200m, dueOn: new DateOnly(2026, 10, 19));

    private DecisionRow Iban() => _decisions.Add(
        "iban.a-verifier", new() { ["supplier"] = "Minoterie Dupuis", ["changedOn"] = "2026-10-18" }, DecisionUrgencyDto.Critical);

    private DecisionRow Fiscal() => _decisions.Add(
        "fiscal.echeance",
        new() { ["obligation"] = "@Tax.Obligation.VatReturnCa3", ["periodEnd"] = "2026-09-30", ["dueOn"] = "2026-10-21" },
        DecisionUrgencyDto.High, dueOn: new DateOnly(2026, 10, 21), audience: DecisionAudienceDto.Cabinet);

    [Fact]
    public void Les_decisions_a_prendre_s_affichent_les_plus_urgentes_d_abord_avec_leurs_phrases()
    {
        Iban();
        Fiscal();
        DecisionRow reminder = Reminder();
        _decisions.Add("attestation.a-renouveler", new() { ["supplier"] = "Plâtrerie", ["status"] = "@Decision.attestation.a-renouveler.Status.Expired", ["contractAmount"] = "8000.00" },
            status: DecisionStatusDto.Snoozed, snoozedUntil: new DateOnly(2026, 10, 26));
        _decisions.Add("lcb-ft.fiche", new() { ["status"] = "@Decision.lcb-ft.fiche.Status.Missing" }, status: DecisionStatusDto.Dismissed, audience: DecisionAudienceDto.Cabinet);

        IRenderedComponent<DecisionsPage> page = Open();

        page.Find("h1").TextContent.Should().Be("Décisions à prendre");
        _decisions.Refreshes.Should().Be(0, "les décisions ont été actualisées il y a une heure");
        (page.Find("#decisions-open").TextContent, page.Find("#decisions-urgent").TextContent, page.Find("#decisions-snoozed").TextContent, page.Find("#decisions-done").TextContent)
            .Should().Be(("3", "2", "1", "1"));
        Titles(page).Should().Equal(
            "Vérifier le nouvel IBAN de Minoterie Dupuis",
            "Déclaration de TVA (CA3) à déposer avant le 21/10/2026",
            "Relancer Durand SARL pour la facture F-1");
        page.Find($"#decision-{reminder.Id} .lt-decision-reason").TextContent.Should()
            .Be("Rappel amical : la facture a 20 jours de retard et le scénario de relance l'envoie aujourd'hui.");
        page.Find($"#decision-{reminder.Id} .lt-decision-impact").TextContent.Should().Contain("1").And.Contain("200,00");
        page.Find($"#decision-{reminder.Id} .lt-decision-due").TextContent.Trim().Should().Be("à faire aujourd'hui");
        page.FindAll(".lt-decision-due").Select(d => d.TextContent.Trim()).Should().Contain("avant le 21/10/2026");
        page.Find($"#decision-accept-{reminder.Id}").TextContent.Trim().Should().Be("Envoyer la relance");
        page.FindAll("[id^='decision-accept-']").Should().ContainSingle("seule la relance a une action préparée");
        page.Find($"#decision-open-{reminder.Id}").GetAttribute("href").Should().Be("somewhere");
        page.FindAll("#decisions-rules li").Should().HaveCount(10);
        page.Find("#decisions-rules").TextContent.Should().Contain("Radar de prévention : le dossier passe « à appeler »");

        page.Find("#decisions-filter-status").Change("all");
        Titles(page).Should().HaveCount(5);
        page.Find("#decisions-list").TextContent.Should().Contain("Attestation de vigilance expirée : Plâtrerie").And.Contain("Revient le 26/10/2026").And.Contain("Ignorée");
        page.Find("#decisions-filter-audience").Change("Cabinet");
        Titles(page).Should().Equal("Déclaration de TVA (CA3) à déposer avant le 21/10/2026", "Fiche anti-blanchiment du client à remplir");
        page.Find("#decisions-filters-reset").Click();
        page.Find("#decisions-search").Input("dupuis");
        Titles(page).Should().Equal("Vérifier le nouvel IBAN de Minoterie Dupuis");
        page.Find("#decisions-search").Input("aucune");
        page.FindAll("#decisions-filtered-empty").Should().ContainSingle();
        page.Find("#decisions-search").Input(string.Empty);
        page.Find("#decisions-filter-urgency").Change("Normal");
        Titles(page).Should().Equal("Relancer Durand SARL pour la facture F-1");
        page.Find("#decisions-filter-urgency").Change(string.Empty);
        page.Find("#decisions-sort").Change("Impact:Descending");
        Titles(page)[0].Should().Be("Relancer Durand SARL pour la facture F-1", "seule la relance a un montant en jeu");
        page.Find("#decisions-sort").Change("DueOn:Ascending");
        Titles(page)[0].Should().Be("Relancer Durand SARL pour la facture F-1", "elle est due aujourd'hui");
    }

    [Fact]
    public void Un_libelle_peut_porter_les_faits_de_la_decision()
    {
        _decisions.Add(
            "seuil.proche",
            new()
            {
                ["what"] = "@Decision.seuil.proche.What.SubcontractorVigilance",
                ["party"] = "Plâtrerie Dupuis",
                ["state"] = "@Decision.seuil.proche.State.Crossed",
                ["on"] = "2026-09-01",
                ["change"] = "@Thresholds.Change.vigilance.sous-traitant",
                ["basis"] = "@Thresholds.Basis.ExclTax",
                ["thresholdAmount"] = "5000.00",
                ["toDateAmount"] = "6200.00",
                ["projectedAmount"] = "8100.00",
            },
            DecisionUrgencyDto.High);

        IRenderedComponent<DecisionsPage> page = Open();

        Titles(page).Should().Equal("Attestation de vigilance de Plâtrerie Dupuis (franchi le 01/09/2026)");
        page.Find(".lt-decision-reason").TextContent.Should().StartWith("Dès ce montant, demandez à ce fournisseur son attestation")
            .And.Contain("Cumul : 6").And.Contain("HT pour un seuil de 5").And.EndWith("à la fin de la période.");
    }

    [Fact]
    public void Au_dela_de_vingt_decisions_la_liste_se_lit_par_pages()
    {
        for (int i = 1; i <= 25; i++)
        {
            _decisions.Add("iban.a-verifier", new() { ["supplier"] = $"Fournisseur {i:00}", ["changedOn"] = "2026-10-18" });
        }

        IRenderedComponent<DecisionsPage> page = Open();

        Titles(page).Should().HaveCount(20);
        page.Find("#decisions-pager").TextContent.Should().Contain("1").And.Contain("2");
        page.FindAll("#decisions-pager button")[1].Click();
        Titles(page).Should().HaveCount(5).And.Contain("Vérifier le nouvel IBAN de Fournisseur 25");
        page.Find("#decisions-search").Input("fournisseur 0");
        Titles(page).Should().HaveCount(9, "un filtre revient à la première page");
        page.FindAll("#decisions-pager").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(7, 1)]
    [InlineData(1, 0)]
    public void L_ecran_actualise_des_decisions_jamais_actualisees_ou_trop_anciennes(int? hoursAgo, int refreshes)
    {
        _decisions.RefreshedAt = hoursAgo is { } hours ? FakeDecisionsGateway.Now.AddHours(-hours) : null;

        IRenderedComponent<DecisionsPage> page = Open();

        _decisions.Refreshes.Should().Be(refreshes);
        page.Find("#decisions-refreshed").TextContent.Should().StartWith("Actualisé");
        page.FindAll("#decisions-empty").Should().ContainSingle();
        page.Find("#decisions-refresh").Click();
        _decisions.Refreshes.Should().Be(refreshes + 1);
    }

    [Fact]
    public void Accepter_fait_l_action_preparee_avec_la_version_lue()
    {
        DecisionRow reminder = Reminder();
        IRenderedComponent<DecisionsPage> page = Open();

        page.Find($"#decision-accept-{reminder.Id}").Click();

        _decisions.Answers.Should().ContainSingle().Which.Should().Match<(Guid Id, AnswerDecisionRequest Request, long? Version)>(a =>
            a.Id == reminder.Id && a.Request.Choice == DecisionChoiceDto.Accept && a.Version == 2);
        this.ToastText().Should().Be("Fait : Relancer Durand SARL pour la facture F-1.");
        this.Toasts()[^1].Action.Should().BeNull("un e-mail parti ne se reprend pas");
        page.WaitForAssertion(() => page.FindAll("#decisions-empty, #decisions-filtered-empty").Should().ContainSingle());
        page.Find("#decisions-done").TextContent.Should().Be("1");
    }

    [Fact]
    public async Task Plus_tard_et_ignorer_se_defont_depuis_le_message()
    {
        DecisionRow iban = Iban();
        DecisionRow reminder = Reminder();
        IRenderedComponent<DecisionsPage> page = Open();

        page.Find($"#decision-snooze-{iban.Id}").Click();
        page.Find($"#decision-snooze-{iban.Id}-7").Click();

        _decisions.Answers[^1].Request.Should().Be(new AnswerDecisionRequest { Choice = DecisionChoiceDto.Snooze, SnoozeUntil = new DateOnly(2026, 10, 26) });
        this.ToastText().Should().Be("Remise au 26/10/2026.");
        Titles(page).Should().Equal("Relancer Durand SARL pour la facture F-1");
        Toast snoozed = this.Toasts()[^1];
        snoozed.Action!.Label.Should().Be("Annuler");
        await page.InvokeAsync(snoozed.Action.Run);
        _decisions.Answers[^1].Should().Match<(Guid Id, AnswerDecisionRequest Request, long? Version)>(a =>
            a.Id == iban.Id && a.Request.Choice == DecisionChoiceDto.Restore && a.Version == 3);
        page.WaitForAssertion(() => Titles(page).Should().HaveCount(2));

        page.Find($"#decision-dismiss-{reminder.Id}").Click();
        this.ToastText().Should().Be("Décision ignorée : elle ne sera plus proposée.");
        this.Toasts()[^1].Action.Should().NotBeNull();
        page.Find("#decisions-filter-status").Change("closed");
        page.Find($"#decision-restore-{reminder.Id}").Click();
        this.ToastText().Should().Be("Décision remise dans la liste.");
        _decisions.Rows.Single(r => r.Id == reminder.Id).Status.Should().Be(DecisionStatusDto.Proposed);
    }

    [Fact]
    public void Une_decision_changee_entre_temps_est_relue_et_un_refus_s_affiche_dans_le_message()
    {
        DecisionRow iban = Iban();
        IRenderedComponent<DecisionsPage> page = Open();

        _decisions.NextStale = true;
        page.Find($"#decision-dismiss-{iban.Id}").Click();
        this.ToastText().Should().Be("Cette décision a changé entre-temps : la liste est à jour, relisez-la.");

        _decisions.NextError = ErrorCodes.Decision.NotOpen;
        page.Find($"#decision-dismiss-{iban.Id}").Click();
        this.ToastText().Should().Be("Cette décision a déjà été traitée.");
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
        Titles(page).Should().ContainSingle();
    }

    [Fact]
    public void Le_manager_regle_le_cadran_et_un_refus_le_laisse_tel_quel()
    {
        Reminder();
        IRenderedComponent<DecisionsPage> page = Open();

        page.Find("#decisions-level-Show").Change(true);

        _decisions.Policies.Should().Equal((AutonomyLevelDto.Show, 4L));
        this.ToastText().Should().Be("Cadran réglé : Montrer seulement.");
        page.Find("label[for='decisions-level-Show']").ClassList.Should().Contain("is-current");

        _decisions.NextError = ErrorCodes.Security.Forbidden;
        page.Find("#decisions-level-Prepare").Change(true);
        this.Toasts()[^1].Kind.Should().Be(ToastKind.Error);
        page.Find("label[for='decisions-level-Show']").ClassList.Should().Contain("is-current");
        page.FindAll("#decisions-dial-readonly").Should().BeEmpty();
    }

    [Fact]
    public void Sans_le_droit_de_regler_le_cadran_il_est_en_lecture_seule()
    {
        _decisions.CanConfigure = false;

        IRenderedComponent<DecisionsPage> page = Open();

        page.Find(".lt-decisions-levels").HasAttribute("disabled").Should().BeTrue();
        page.Find("#decisions-dial-readonly").TextContent.Should().Contain("manager");
    }
}
