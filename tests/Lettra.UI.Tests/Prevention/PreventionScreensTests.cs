using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Common;
using Lettra.Contracts.Identity;
using Lettra.Contracts.Insights;
using Lettra.UI.Abstractions;
using Lettra.UI.Shared;
using Lettra.UI.Shared.Components.Prevention;
using Lettra.UI.Shared.Localization;
using Lettra.UI.Shared.Pages.Cabinet;
using Lettra.UI.Shared.Pages.Prevention;
using Lettra.UI.Shared.Services;
using Lettra.UI.Tests.Billing;
using Lettra.UI.Tests.Cabinet;
using Lettra.UI.Tests.Insights;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Tests.Prevention;

/// <summary>
/// Radar de prévention vu du cabinet : la liste des dossiers par niveau d'attention, le détail d'un dossier (signaux,
/// seuils, courrier type) et la carte de l'accueil, comme un collaborateur ou un manager les utilise.
/// </summary>
[Collection("culture")]
public sealed class PreventionScreensTests : BunitContext
{
    private static readonly Guid Pharmacie = new("0199e3a0-0000-7000-8000-0000000b0004");
    private readonly FakePreventionGateway _prevention = new();
    private readonly FakeCabinetGateway _cabinet = new();
    private readonly FakeFileDownloader _files = new();
    private readonly SwitchableDossierSelection _selection = new(FakeCabinetGateway.Boulangerie);

    public PreventionScreensTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLettraUi();
        Services.AddCabinetFakes(_cabinet);
        Services.AddSingleton<IDossierSelection>(_selection);
        Services.AddSingleton<IPreventionGateway>(_prevention);
        Services.AddSingleton<IFileDownloader>(_files);
        Services.AddSingleton<ISessionGateway>(new FakeSessionGateway(new SessionResponse(true, SessionModeDto.Local, "Claire Martin", true)));
        Services.AddSingleton<TimeProvider>(new ParisClock(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero)));
        LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));
    }

    private static CabinetOverviewResponse Overview()
    {
        DateTimeOffset at = new(2026, 11, 3, 2, 0, 0, TimeSpan.Zero);
        static DossierIndicatorDto Radar(int count, IndicatorLevelDto level, DateOnly? due) => new(DossierIndicatorKeys.Prevention, count, level, due);
        return new CabinetOverviewResponse(
        [
            new(FakeCabinetGateway.Boulangerie, "Boulangerie Martin", at, IndicatorLevelDto.Ok, [Radar(0, IndicatorLevelDto.Ok, null)]),
            new(FakeCabinetGateway.Atelier, "Atelier Éole", at, IndicatorLevelDto.Warning, [Radar(1, IndicatorLevelDto.Warning, new DateOnly(2026, 12, 20))]),
            new(FakeCabinetGateway.Garage, "Garage du Centre", at, IndicatorLevelDto.Critical, [Radar(4, IndicatorLevelDto.Critical, new DateOnly(2026, 11, 16))]),
            // Sans le droit de lire le radar de ce dossier, la vue cabinet n'en a pas la colonne : il ne figure pas au radar.
            new(Pharmacie, "Pharmacie du Port", at, IndicatorLevelDto.Ok, [new DossierIndicatorDto(DossierIndicatorKeys.OverdueInvoices, 0, IndicatorLevelDto.Ok, null)]),
        ]);
    }

    private static IReadOnlyList<string> Names(IRenderedComponent<PreventionRadarPage> page) =>
        [.. page.FindAll(".lt-prevention-table tbody .lt-cabinet-dossier").Select(n => n.TextContent)];

    [Fact]
    public void Le_radar_du_cabinet_classe_les_dossiers_par_niveau_d_attention_sans_ceux_qu_on_ne_peut_pas_lire()
    {
        _cabinet.Overview = Overview();

        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();

        page.WaitForAssertion(() => Names(page).Should().Equal("Garage du Centre", "Atelier Éole", "Boulangerie Martin"));
        page.Find("#prevention-radar-kpi-call .lt-kpi-value").TextContent.Should().Be("1");
        page.Find("#prevention-radar-kpi-watch .lt-kpi-value").TextContent.Should().Be("1");
        page.Find("#prevention-radar-kpi-calm .lt-kpi-value").TextContent.Should().Be("1");
        IReadOnlyList<IElement> rows = page.FindAll(".lt-prevention-table tbody tr");
        rows.Select(r => r.GetAttribute("data-tone")).Should().Equal("Call", "Watch", "Calm");
        rows.Select(r => r.QuerySelector(".lt-prevention-badge")!.TextContent).Should().Equal("À appeler", "À surveiller", "Calme");
        rows[0].QuerySelector("td[data-label='Échéance']")!.TextContent.Should().Be("16/11/2026");
        rows[2].QuerySelector("td[data-label='Échéance']")!.TextContent.Should().Be("—", "un dossier calme n'a pas d'échéance");
        rows[2].GetAttribute("aria-current").Should().Be("true", "c'est le dossier ouvert");
        page.Markup.Should().NotContain("Pharmacie du Port");
    }

    [Fact]
    public void Le_radar_du_cabinet_se_filtre_par_niveau_et_par_nom_puis_se_trie()
    {
        _cabinet.Overview = Overview();
        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();
        page.WaitForAssertion(() => Names(page).Should().HaveCount(3));

        page.Find("#prevention-radar-level").Change(nameof(PreventionLevelDto.Watch));
        Names(page).Should().Equal("Atelier Éole");

        page.Find("#prevention-radar-level").Change(string.Empty);
        page.Find("#prevention-radar-search").Input("boulang");
        Names(page).Should().Equal("Boulangerie Martin");

        page.Find("#prevention-radar-filters-reset").Click();
        page.Find("#prevention-radar-sort-dossier").Click();
        Names(page).Should().Equal("Atelier Éole", "Boulangerie Martin", "Garage du Centre");

        page.Find("#prevention-radar-search").Input("introuvable");
        page.Find("#prevention-radar-filtered-empty").Should().NotBeNull();
        page.Find("#prevention-radar-empty-reset").Click();
        Names(page).Should().HaveCount(3);
    }

    [Fact]
    public void Ouvrir_un_dossier_depuis_le_radar_change_de_dossier_et_montre_ses_signaux()
    {
        _cabinet.Overview = Overview();
        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();
        page.WaitForAssertion(() => Names(page).Should().HaveCount(3));

        page.FindAll(".lt-prevention-table tbody tr")[0].QuerySelector(".lt-col-actions button")!.Click();

        page.WaitForAssertion(() => _selection.CurrentDossierId.Should().Be(FakeCabinetGateway.Garage));
        this.ToastText().Should().Be("Dossier « Garage du Centre » ouvert.");
        Services.GetRequiredService<NavigationManager>().Uri.Should().Be("http://localhost/prevention");
    }

    [Fact]
    public void Recalculer_le_radar_du_cabinet_le_dit_dans_le_toaster()
    {
        _cabinet.Overview = Overview();
        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();
        page.WaitForAssertion(() => Names(page).Should().HaveCount(3));

        page.Find("#prevention-radar-refresh").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Radar recalculé."));
        _cabinet.Refreshes.Should().Be(1);
    }

    [Fact]
    public void Sans_aucun_radar_lisible_la_page_l_explique()
    {
        _cabinet.Overview = new CabinetOverviewResponse([]);

        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();

        page.WaitForAssertion(() => page.Find("#prevention-radar-empty").TextContent.Should().Contain("Aucun radar à afficher"));
    }

    [Fact]
    public void Un_tableau_calcule_avant_le_radar_est_recalcule_une_fois_sans_bruit()
    {
        _cabinet.Overview = new CabinetOverviewResponse(
            [new(FakeCabinetGateway.Garage, "Garage du Centre", null, IndicatorLevelDto.Ok, [])]);

        IRenderedComponent<PreventionRadarPage> page = Render<PreventionRadarPage>();

        page.WaitForAssertion(() => page.Find("#prevention-radar-empty").Should().NotBeNull());
        _cabinet.Refreshes.Should().Be(1);
        this.Toasts().Should().BeEmpty("le calcul automatique ne fait pas de bruit");
    }

    [Fact]
    public void La_vue_cabinet_mene_au_radar_quand_elle_en_a_la_colonne()
    {
        _cabinet.Overview = Overview();

        IRenderedComponent<CabinetPage> page = Render<CabinetPage>();

        page.WaitForAssertion(() => page.Find("#cabinet-prevention").GetAttribute("href").Should().Be("cabinet/prevention"));
    }

    [Fact]
    public void Le_radar_du_dossier_met_les_signaux_allumes_en_tete_avec_leur_raison_leur_effet_et_leur_echeance()
    {
        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();

        page.WaitForAssertion(() => page.Find("#prevention-level").TextContent.Should().Be("À appeler"));
        page.Find("#prevention-summary").TextContent.Should().Be("Signaux allumés : 3. Un échange avec le dirigeant est conseillé. Non évalués faute de données : 2.");
        page.Find("#prevention-head").GetAttribute("data-tone").Should().Be("Call");
        page.FindAll(".lt-prevention-signal").Select(s => s.Id).Should().Equal(
            "prevention-signal-treasury",
            "prevention-signal-paymentdelay",
            "prevention-signal-customersinproceedings",
            "prevention-signal-vatbeyondcollections",
            "prevention-signal-topcustomer",
            "prevention-signal-billedcollectedgap",
            "prevention-signal-peergap");

        IElement treasury = page.Find("#prevention-signal-treasury");
        treasury.QuerySelector(".lt-prevention-badge")!.TextContent.Should().Be("À traiter vite");
        treasury.QuerySelector(".lt-prevention-sentence")!.TextContent.Should()
            .StartWith("Le solde prévu passe sous zéro la semaine du 16 novembre 2026")
            .And.EndWith("au 2 novembre 2026, sur 8 semaines).");
        treasury.QuerySelector(".lt-prevention-facts")!.TextContent.Should().Contain("Manque au plus bas").And.Contain("sous zéro à partir du 16 novembre 2026");
        treasury.QuerySelectorAll(".lt-prevention-mini tbody tr").Should().HaveCount(3);
        treasury.QuerySelectorAll(".lt-prevention-mini tbody tr.is-negative").Should().ContainSingle();
        treasury.QuerySelector("#prevention-action-treasury")!.GetAttribute("href").Should().Be("what-if");

        IElement delay = page.Find("#prevention-signal-paymentdelay");
        delay.QuerySelector(".lt-prevention-sentence")!.TextContent.Should().Contain("de 30 j à 45 j (+15 j, seuil 5 j)");
        delay.QuerySelectorAll(".lt-prevention-mini thead th").Select(t => t.TextContent).Should().Equal("Mois", "Délai moyen de paiement");
        delay.QuerySelector("#prevention-action-paymentdelay")!.GetAttribute("href").Should().Be("payment-weather");

        IElement proceedings = page.Find("#prevention-signal-customersinproceedings");
        proceedings.QuerySelector(".lt-prevention-parties a")!.GetAttribute("href").Should().Be($"customers/{FakePreventionGateway.Fournil}");
        proceedings.QuerySelector(".lt-prevention-parties")!.TextContent.Should().Contain("publié le 20/10/2026");
        proceedings.QuerySelector(".lt-prevention-facts")!.TextContent.Should().Contain("déclarer la créance avant le 20 décembre 2026");

        page.Find("#prevention-signal-vatbeyondcollections .lt-prevention-sentence").TextContent.Should().StartWith("Déclaration de TVA (CA3), période du 1 octobre 2026 au 31 octobre 2026");
        page.Find("#prevention-signal-vatbeyondcollections").QuerySelector(".lt-prevention-facts").Should().BeNull("un signal calme n'a ni effet ni échéance");
        page.Find("#prevention-signal-topcustomer .lt-prevention-sentence").TextContent.Should().Be("Pas de chiffre d'affaires sur les douze derniers mois.");
        page.Find("#prevention-signal-peergap .lt-prevention-sentence").TextContent.Should().Be("Ce signal est désactivé dans les seuils du dossier.");
        page.Find("#prevention-letter-block").GetAttribute("data-open").Should().Be("true", "un dossier à appeler ouvre le courrier");
    }

    [Fact]
    public void Le_manager_saisit_le_solde_regle_les_seuils_et_le_radar_est_recalcule()
    {
        PreventionSettingsResponse noCash = FakePreventionGateway.DefaultSettings with { CashBalance = null, CashBalanceOn = null, Version = 0 };
        PreventionSignalResponse treasury = new(PreventionSignalDto.Treasury, PreventionSignalStateDto.NotEvaluated, PreventionUnavailableDto.NoCashBalance, null, null, [], [], [], []);
        _prevention.Radar = FakePreventionGateway.Build(noCash, canAdjust: true, treasury, FakePreventionGateway.Vat);
        _prevention.RadarAfterSave = FakePreventionGateway.Build(FakePreventionGateway.DefaultSettings, canAdjust: true);
        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();
        page.WaitForAssertion(() => page.Find("#prevention-level").TextContent.Should().Be("Calme"));
        page.Find("#prevention-settings").GetAttribute("data-open").Should().Be("true", "sans solde, les seuils s'ouvrent pour le saisir");
        page.Find("#prevention-letter-block").GetAttribute("data-open").Should().Be("false");

        page.Find("#prevention-action-treasury").Click();
        JSInterop.VerifyFocusAsyncInvoke();

        page.Find("#prevention-cash").Change("8000.5");
        page.Find("#prevention-call-signals").Change("4");
        page.Find("#prevention-follow-peergap").Change(true);
        page.Find("#prevention-follow-topcustomer").Change(false);
        page.Find("#prevention-settings-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Seuils enregistrés : le radar est recalculé."));
        UpdatePreventionSettingsRequest saved = _prevention.Saved.Should().ContainSingle().Subject;
        saved.CashBalance.Should().Be(8_000.5m);
        saved.CashBalanceOn.Should().Be(FakePreventionGateway.Today, "la date du solde est proposée au jour du calcul");
        saved.CallSignalCount.Should().Be(4);
        saved.Disabled.Should().Equal(PreventionSignalDto.TopCustomer);
        saved.Version.Should().Be(0);
        _prevention.RadarReads.Should().Be(2, "le radar est relu avec les nouveaux seuils");
        page.WaitForAssertion(() => page.Find("#prevention-level").TextContent.Should().Be("À appeler"));
    }

    [Fact]
    public void Un_seuil_refuse_par_le_serveur_s_affiche_sous_son_champ_et_les_valeurs_proposees_reviennent()
    {
        _prevention.SaveErrors = [new ErrorDetail(ErrorCodes.Prevention.ThresholdInvalid, nameof(UpdatePreventionSettingsRequest.CallSignalCount))];
        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();
        page.WaitForAssertion(() => page.Find("#prevention-settings-form").Should().NotBeNull());

        page.Find("#prevention-call-signals").Change("9");
        page.Find("#prevention-settings-save").Click();

        page.WaitForAssertion(() => this.ToastText().Should().Be("Valeur hors des bornes permises."));
        page.Find("#prevention-call-signals").ParentElement!.TextContent.Should().Contain("Valeur hors des bornes permises.");

        page.Find("#prevention-delay-rise").Change("30");
        page.Find("#prevention-settings-defaults").Click();
        page.Find("#prevention-call-signals").GetAttribute("value").Should().Be("3");
        page.Find("#prevention-delay-rise").GetAttribute("value").Should().Be("5");
        page.Find("#prevention-cash").GetAttribute("value").Should().Be("8000", "le solde saisi reste");
        page.Find("#prevention-call-signals").ParentElement!.TextContent.Should().NotContain("Valeur hors des bornes permises.");
    }

    [Fact]
    public void Le_collaborateur_lit_les_seuils_sans_pouvoir_les_regler()
    {
        PreventionSignalResponse outdated = new(
            PreventionSignalDto.Treasury, PreventionSignalStateDto.NotEvaluated, PreventionUnavailableDto.CashBalanceOutdated, null, null,
            [new("cashBalanceOn", PreventionFigureKindDto.Date, Date: new DateOnly(2026, 9, 1))], [], [], []);
        _prevention.Radar = FakePreventionGateway.Build(FakePreventionGateway.DefaultSettings, canAdjust: false, outdated, FakePreventionGateway.PaymentDelay);

        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();

        page.WaitForAssertion(() => page.Find("#prevention-level").TextContent.Should().Be("À surveiller"));
        page.Find("#prevention-settings-readonly").TextContent.Should().Be("Seuls les responsables du dossier règlent ces seuils.");
        page.FindAll("#prevention-settings-form").Should().BeEmpty();
        page.FindAll("#prevention-action-treasury").Should().BeEmpty("sans le droit de régler, pas de bouton vers les seuils");
        page.Find("#prevention-signal-treasury .lt-prevention-sentence").TextContent.Should().Be("Le solde bancaire saisi date du 1 septembre 2026, il y a plus de 45 jours : saisissez un solde récent.");
        page.Find("#prevention-settings").GetAttribute("data-open").Should().Be("false");
    }

    [Fact]
    public void Un_refus_de_lecture_du_radar_s_affiche_sans_squelette_sans_fin()
    {
        _prevention.RadarErrors = [new ErrorDetail(ErrorCodes.Security.Forbidden)];

        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();

        page.WaitForAssertion(() => this.Toasts().Should().ContainSingle(t => t.Kind == ToastKind.Error));
        page.FindAll(".lt-skeleton, .lt-explorer-skeleton").Should().BeEmpty();
        page.FindAll("#prevention-signals").Should().BeEmpty();
    }

    [Fact]
    public void Le_courrier_se_prepare_en_anglais_se_relit_et_part_par_e_mail()
    {
        IRenderedComponent<PreventionLetterPanel> panel = Render<PreventionLetterPanel>(p => p.Add(c => c.Today, FakePreventionGateway.Today));

        panel.Find("#prevention-letter-language").Change("en-US");
        panel.Find("#prevention-letter-prepare").Click();

        panel.WaitForAssertion(() => panel.Find("#prevention-letter-subject").GetAttribute("value").Should().StartWith("Meeting proposal"));
        _prevention.LetterLanguages.Should().Equal("en-US");
        panel.FindAll("#prevention-letter-no-email").Should().BeEmpty();

        panel.Find("#prevention-letter-to").Change("dirigeant@boulangerie-martin.test");
        panel.Find("#prevention-letter-body").Change("Dear Sir or Madam,\n\nLet us meet next week.");
        panel.Find("#prevention-letter-send").Click();

        panel.WaitForAssertion(() => this.ToastText().Should().Be("Courrier envoyé à dirigeant@boulangerie-martin.test."));
        SendPreventionLetterRequest sent = _prevention.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be("dirigeant@boulangerie-martin.test");
        sent.Subject.Should().StartWith("Meeting proposal");
        sent.Body.Should().EndWith("Let us meet next week.");

        // Revenir au texte proposé garde le destinataire saisi.
        panel.Find("#prevention-letter-reset").Click();
        panel.WaitForAssertion(() => panel.Find("#prevention-letter-body").GetAttribute("value").Should().Contain("We follow the activity"));
        panel.Find("#prevention-letter-to").GetAttribute("value").Should().Be("dirigeant@boulangerie-martin.test");
    }

    [Fact]
    public void Un_destinataire_manquant_s_affiche_sous_son_champ()
    {
        _prevention.SendErrors = [new ErrorDetail(ErrorCodes.Prevention.RecipientRequired, nameof(SendPreventionLetterRequest.To))];
        IRenderedComponent<PreventionLetterPanel> panel = Render<PreventionLetterPanel>(p => p.Add(c => c.Today, FakePreventionGateway.Today));
        panel.Find("#prevention-letter-prepare").Click();
        panel.WaitForAssertion(() => panel.Find("#prevention-letter-send").Should().NotBeNull());

        panel.Find("#prevention-letter-send").Click();

        panel.WaitForAssertion(() => this.ToastText().Should().Be("Saisissez l'e-mail du destinataire."));
        panel.Find("#prevention-letter-to").ParentElement!.TextContent.Should().Contain("Saisissez l'e-mail du destinataire.");
        _prevention.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Sans_serveur_d_e_mail_le_courrier_se_telecharge_en_pdf_ou_se_copie()
    {
        _prevention.CanSend = false;
        IRenderedComponent<PreventionLetterPanel> panel = Render<PreventionLetterPanel>(p => p.Add(c => c.Today, FakePreventionGateway.Today));
        panel.Find("#prevention-letter-prepare").Click();
        panel.WaitForAssertion(() => panel.Find("#prevention-letter-no-email").Should().NotBeNull());
        panel.FindAll("#prevention-letter-to, #prevention-letter-send").Should().BeEmpty();

        panel.Find("#prevention-letter-pdf").Click();

        panel.WaitForAssertion(() => _files.Files.Should().ContainSingle());
        (string name, string mime, byte[] content) = _files.Files[0];
        name.Should().Be("rendez-vous-prevention-2026-11-03.pdf");
        mime.Should().Be("application/pdf");
        content.Take(4).Should().Equal("%PDF"u8.ToArray());
        this.ToastText().Should().Be("Courrier téléchargé en PDF.");

        panel.Find("#prevention-letter-copy").Click();
        panel.WaitForAssertion(() => this.ToastText().Should().Be("Texte copié."));
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "navigator.clipboard.writeText"
            && ((string)i.Arguments[0]!).StartsWith("Proposition de rendez-vous", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_copie_refusee_par_le_navigateur_le_dit_sans_erreur()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        IRenderedComponent<PreventionLetterPanel> panel = Render<PreventionLetterPanel>(p => p.Add(c => c.Today, FakePreventionGateway.Today));
        panel.Find("#prevention-letter-prepare").Click();
        panel.WaitForAssertion(() => panel.Find("#prevention-letter-copy").Should().NotBeNull());

        panel.Find("#prevention-letter-copy").Click();

        panel.WaitForAssertion(() => this.Toasts().Should().Contain(t => t.Kind == ToastKind.Warning && t.Message.StartsWith("Copie refusée", StringComparison.Ordinal)));
    }

    [Fact]
    public void La_carte_de_l_accueil_resume_le_radar_pour_le_cabinet()
    {
        IRenderedComponent<PreventionCard> card = Render<PreventionCard>();

        card.WaitForAssertion(() => card.Find("#home-prevention").GetAttribute("data-tone").Should().Be("Call"));
        card.Find(".lt-prevention-badge").TextContent.Should().Be("À appeler");
        card.Find(".lt-prevention-card-level .lettra-hint").TextContent.Should().Be("échéance la plus proche : 16/11/2026");
        card.FindAll(".lt-prevention-card-list li").Select(l => l.TextContent.Trim()).Should().Equal(
            "Trésorerie des prochaines semaines", "Délais de paiement des clients", "Clients en procédure collective");
        card.Find("#home-prevention-open").GetAttribute("href").Should().Be("prevention");
    }

    [Fact]
    public void La_carte_d_un_dossier_calme_le_dit_en_une_ligne()
    {
        _prevention.Summary = new PreventionSummaryResponse(true, PreventionLevelDto.Calm, 0, [], null);

        IRenderedComponent<PreventionCard> card = Render<PreventionCard>();

        card.WaitForAssertion(() => card.Find(".lt-panel-ok").TextContent.Trim().Should().Be("Aucun signal allumé."));
        card.FindAll(".lt-prevention-card-level .lettra-hint").Should().BeEmpty();
    }

    [Fact]
    public void La_carte_reste_cachee_pour_le_client_du_portail()
    {
        _prevention.Summary = PreventionSummaryResponse.Hidden;

        IRenderedComponent<PreventionCard> card = Render<PreventionCard>();

        card.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Un_hote_sans_radar_ne_montre_pas_la_carte()
    {
        BunitContext bare = new();
        bare.JSInterop.Mode = JSRuntimeMode.Loose;
        bare.Services.AddLettraUi();

        IRenderedComponent<PreventionCard> card = bare.Render<PreventionCard>();

        card.Markup.Trim().Should().BeEmpty();
        bare.Dispose();
    }

    [Fact]
    public void Le_radar_du_dossier_se_lit_en_anglais()
    {
        LettraCultures.Apply(CultureInfo.GetCultureInfo("en-US"));

        IRenderedComponent<PreventionPage> page = Render<PreventionPage>();

        page.WaitForAssertion(() => page.Find("#prevention-level").TextContent.Should().Be("Call the client"));
        page.Find("#prevention-signal-treasury .lt-prevention-sentence").TextContent.Should().StartWith("The forecast balance goes below zero in the week of 16 November 2026");
        page.Find("#prevention-signal-treasury .lt-prevention-facts").TextContent.Should().Contain("below zero from 16 November 2026");
        page.Find("#prevention-signal-paymentdelay .lt-prevention-sentence").TextContent.Should().Contain("15 d");
    }
}
