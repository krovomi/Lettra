using Lettra.Ai.Local;
using Lettra.Ai.Tools.Tools;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Features.Billing;
using Lettra.Application.Features.Customers;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using static Lettra.Ai.Tests.ScriptedTurnsEngine;

namespace Lettra.Ai.Tests;

/// <summary>
/// Points 1 et 3 (annexe D) : brouillon de devis en langage naturel et « Pourquoi ? » sur la checklist. Les outils passent
/// par les cas d'usage ; l'IA crée au plus un brouillon, ne valide ni n'émet jamais, et ne lit que le document ouvert.
/// </summary>
public sealed class AssistantBillingToolsTests
{
    private static readonly Guid CustomerId = Guid.Parse("0199e3a0-0000-7000-8000-00000000c001");
    private static readonly Guid QuoteGuid = Guid.Parse("0199e3a0-0000-7000-8000-00000000d001");

    private static readonly object DraftInput = new
    {
        client_id = CustomerId.ToString(),
        objet = "Mission de conseil",
        lignes = new object[]
        {
            new { designation = "Conseil", quantite = 3, prix_unitaire_ht = 650, taux_tva = "FR-20", unite = "DAY" },
            new { designation = "Formation", quantite = 1, prix_unitaire_ht = 900, taux_tva = "FR-20" },
        },
    };

    [Fact]
    public async Task La_demande_en_langage_naturel_cree_un_brouillon_et_une_validation_demandee_par_le_modele_est_refusee()
    {
        await using AssistantHarness h = new(
            new ScriptedTurnsEngine(
                _ => Call("chercher_client", new { recherche = "Dupont" }),
                _ => Call("creer_brouillon_devis", DraftInput, "c2"),
                _ => Call("valider_devis", new { devis_id = QuoteGuid.ToString() }, "c3"),
                _ => Say("Le brouillon est prêt : 2850.00 € HT. Relisez-le puis validez-le.")),
            AssistantHarness.Enabled());
        AnswerCustomersAndQuotes(h.Dispatcher);

        List<AssistantEvent> events = await h.AskAsync(
            AssistantFeature.DraftFromText, "Devis pour Dupont SARL : 3 jours de conseil à 650 € HT et une formation à 900 €");

        events.OfType<AssistantDraftProposedEvent>().Should().ContainSingle()
            .Which.Should().Be(new AssistantDraftProposedEvent(QuoteGuid, "quote", "Dupont SARL · 2 ligne(s) · 2850.00 € HT · 3420.00 € TTC"));
        events.OfType<AssistantToolUsedEvent>().Should().Contain(new AssistantToolUsedEvent("creer_brouillon_devis", AssistantToolRisk.CreatesDraft, true));
        events.OfType<AssistantWarningEvent>().Should().ContainSingle(w => w.Code == ErrorCodes.Ai.ToolRejected && w.Detail == "valider_devis");
        events.OfType<AssistantWarningEvent>().Should().NotContain(w => w.Code == ErrorCodes.Ai.UngroundedAmounts);

        // L'application n'a reçu qu'une recherche, la création du brouillon et sa relecture : rien d'autre.
        h.Dispatcher.Sent.Select(r => r.GetType()).Should().Equal(typeof(ListCustomersQuery), typeof(CreateQuoteCommand), typeof(GetQuoteQuery));
        CreateQuoteCommand created = h.Dispatcher.Sent.OfType<CreateQuoteCommand>().Single();
        created.Dossier.Should().Be(AssistantHarness.Dossier);
        created.Data.CustomerId.Should().Be(CustomerId);
        created.Data.Subject.Should().Be("Mission de conseil");
        created.Data.Lines.Select(l => (l.Designation, l.Quantity, l.UnitPriceExclTax, l.VatRateCode, l.UnitCode))
            .Should().Equal(("Conseil", 3m, 650m, "FR-20", "DAY"), ("Formation", 1m, 900m, "FR-20", "C62"));

        // Le nom du client reste masqué vers le moteur, et le brouillon est journalisé.
        h.Engine.EverythingSentExceptStableInstructions().Should().NotContain("Dupont SARL");
        (await h.AuditAsync()).Should().Contain(e => e.Kind == AiAuditKind.Draft && e.Detail == $"quote {QuoteGuid}");
    }

    [Fact]
    public async Task Avec_un_modele_local_servi_par_Ollama_le_brouillon_passe_par_le_meme_orchestrateur_sans_cle_ni_donnee_en_clair()
    {
        // Réponses enregistrées d'un Mistral servi par Ollama (API compatible chat completions), sur le poste.
        string[] answers =
        [
            """{ "choices": [ { "finish_reason": "tool_calls", "message": { "role": "assistant", "content": null, "tool_calls": [ { "id": "call_1", "type": "function", "function": { "name": "chercher_client", "arguments": "{\"recherche\":\"Dupont\"}" } } ] } } ] }""",
            """{ "choices": [ { "finish_reason": "tool_calls", "message": { "role": "assistant", "content": null, "tool_calls": [ { "id": "call_2", "type": "function", "function": { "name": "creer_brouillon_devis", "arguments": "{\"client_id\":\"0199e3a0-0000-7000-8000-00000000c001\",\"lignes\":[{\"designation\":\"Conseil\",\"quantite\":3,\"prix_unitaire_ht\":650,\"taux_tva\":\"FR-20\"},{\"designation\":\"Formation\",\"quantite\":1,\"prix_unitaire_ht\":900,\"taux_tva\":\"FR-20\"}]}" } } ] } } ] }""",
            """{ "choices": [ { "finish_reason": "stop", "message": { "role": "assistant", "content": "Brouillon créé pour [CLIENT_1] : 2850.00 € HT. Relisez-le puis validez-le." } } ] }""",
        ];
        int next = 0;
        RecordingHandler ollama = new(_ => RecordingHandler.Json(answers[Math.Min(next++, answers.Length - 1)]));
        using HttpClient http = new(ollama);
        LocalModelEngine local = new(http, new LocalModelOptions { Endpoint = new Uri("http://127.0.0.1:11434/v1/chat/completions"), Model = "mistral-small" });
        await using AssistantHarness h = new(new ScriptedTurnsEngine(r => local.CompleteAsync(r, CancellationToken.None).GetAwaiter().GetResult()), AssistantHarness.Enabled());
        AnswerCustomersAndQuotes(h.Dispatcher);

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.DraftFromText, "Devis pour Dupont SARL : 3 jours de conseil à 650 € et une formation à 900 €");

        events.OfType<AssistantDraftProposedEvent>().Should().ContainSingle().Which.DraftId.Should().Be(QuoteGuid);
        events.OfType<AssistantTextEvent>().Single().Text.Should().Be("Brouillon créé pour Dupont SARL : 2850.00 € HT. Relisez-le puis validez-le.");
        ollama.Requests.Should().HaveCount(3).And.OnlyContain(r => r.Request.RequestUri!.IsLoopback && r.Request.Headers.Authorization == null);
        ollama.Requests.Select(r => r.Body).Should().NotContain(b => b.Contains("Dupont SARL"));
        h.Dispatcher.Sent.OfType<CreateQuoteCommand>().Single().Data.Lines.Sum(l => l.Quantity * l.UnitPriceExclTax).Should().Be(2850m);
    }

    [Fact]
    public async Task Sans_le_droit_de_creer_un_devis_l_outil_de_brouillon_n_est_pas_propose()
    {
        await using AssistantHarness h = new(new ScriptedTurnsEngine(_ => Say("ok")), AssistantHarness.Enabled());
        HashSet<Permission> permissions = [.. AssistantHarness.AllPermissions];
        permissions.Remove(Permission.DevisCreer);

        await h.AskAsync(AssistantFeature.DraftFromText, "Devis pour Dupont", permissions: permissions);

        h.Engine.Requests[0].Tools.Select(t => t.Name).Should().Contain("chercher_client").And.NotContain(CreateQuoteDraftTool.ToolName);
    }

    [Fact]
    public async Task Un_brouillon_refuse_par_l_application_renvoie_ses_codes_au_moteur_sans_rien_creer()
    {
        await using AssistantHarness h = new(
            new ScriptedTurnsEngine(_ => Call("creer_brouillon_devis", DraftInput), _ => Say("Le client n'existe pas.")),
            AssistantHarness.Enabled());
        h.Dispatcher.On<CreateQuoteCommand, Result<QuoteId>>(_ => Result<QuoteId>.Failure([Error.NotFound("Customer.NotFound")]));

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.DraftFromText, "Devis pour Dupont");

        events.Should().NotContain(e => e is AssistantDraftProposedEvent);
        events.OfType<AssistantToolUsedEvent>().Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        h.Engine.Requests[1].Messages[^1].Parts.OfType<ToolResultPart>().Single().Content.Should().Be("Refusé par Lettra : Customer.NotFound");
    }

    [Fact]
    public async Task Pourquoi_relit_la_checklist_du_document_ouvert_et_seulement_lui()
    {
        await using AssistantHarness h = new(
            new ScriptedTurnsEngine(
                _ => Call("controler_document_ouvert", new { }),
                _ => Say("La date d'échéance manque : ajoutez-la avant de valider.")),
            AssistantHarness.Enabled());
        h.Dispatcher
            .On<CheckInvoiceQuery, Result<ComplianceReportResponse>>(_ => Result<ComplianceReportResponse>.Failure([Error.NotFound(InvoiceErrors.NotFoundCode)]))
            .On<CheckQuoteQuery, Result<ComplianceReportResponse>>(_ => new ComplianceReportResponse(true,
            [
                new("Compliance.DueDateMissing", ComplianceSeverityDto.Error, "CGI art. 242 nonies A"),
                new("Compliance.SellerNameMissing", ComplianceSeverityDto.Ok, null),
            ]));

        List<AssistantEvent> events = await h.AskAsync(
            AssistantFeature.ExplainCompliance, "Pourquoi « Date d'échéance absente » ?", openDocumentId: QuoteGuid);

        h.Dispatcher.Sent.OfType<CheckQuoteQuery>().Single().QuoteId.Should().Be(new QuoteId(QuoteGuid));
        h.Dispatcher.Sent.OfType<CheckInvoiceQuery>().Single().InvoiceId.Should().Be(new InvoiceId(QuoteGuid));
        AssistantRuleResultEvent rule = events.OfType<AssistantRuleResultEvent>().Single();
        rule.Tool.Should().Be(CheckOpenDocumentTool.ToolName);
        rule.Content.Should().Contain("Compliance.DueDateMissing").And.Contain("\"bloque_la_validation\":true").And.NotContain("SellerNameMissing");
        events.OfType<AssistantTextEvent>().Single().Text.Should().StartWith("La date d'échéance manque");
    }

    [Fact]
    public async Task Pourquoi_sans_document_ouvert_ne_lit_rien()
    {
        await using AssistantHarness h = new(
            new ScriptedTurnsEngine(_ => Call("controler_document_ouvert", new { }), _ => Say("Ouvrez d'abord la pièce.")),
            AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.ExplainCompliance, "Pourquoi ?");

        h.Dispatcher.Sent.Should().BeEmpty();
        events.OfType<AssistantToolUsedEvent>().Single().Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Les_outils_de_la_facturation_ne_sont_proposes_que_pour_leur_point()
    {
        await using AssistantHarness h = new(new ScriptedTurnsEngine(_ => Say("ok")), AssistantHarness.Enabled());

        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Quel taux pour une formation ?");
        await h.AskAsync(AssistantFeature.ExplainCompliance, "Pourquoi ?", openDocumentId: QuoteGuid);

        h.Engine.Requests[0].Tools.Select(t => t.Name).Should().NotContain(["chercher_client", "chercher_article", "creer_brouillon_devis", "controler_document_ouvert"]);
        h.Engine.Requests[1].Tools.Select(t => t.Name).Should().Contain("controler_document_ouvert").And.NotContain("creer_brouillon_devis");
    }

    private static void AnswerCustomersAndQuotes(RecordingDispatcher dispatcher) => dispatcher
        .On<ListCustomersQuery, Result<PagedResponse<CustomerSummary>>>(_ =>
            new PagedResponse<CustomerSummary>([new(CustomerId, CustomerKindDto.Company, "Dupont SARL", "987654321", "Lyon")], 1, 10, 1))
        .On<CreateQuoteCommand, Result<QuoteId>>(_ => new QuoteId(QuoteGuid))
        .On<GetQuoteQuery, Result<QuoteResponse>>(_ => Quote());

    private static QuoteResponse Quote()
    {
        static DocumentLineResponse Line(int position, string designation, decimal quantity, decimal price) => new(
            position, null, null, designation, null, CatalogItemKindDto.Service, "C62", quantity, price, 0m, "FR-20", 20m,
            VatCategoryDto.Standard, null, quantity * price);

        return new QuoteResponse(
            QuoteGuid, 1, null, QuoteStatusDto.Draft, CustomerId,
            new PartySnapshotResponse("Dupont SARL", "1 place Bellecour", null, "69002", "Lyon", "FR", "987654321", null, null, null, null, null),
            null, null, false, VatRegimeDto.Standard, "Mission de conseil", null,
            [Line(1, "Conseil", 3m, 650m), Line(2, "Formation", 1m, 900m)],
            new DocumentTotalsResponse([new(VatCategoryDto.Standard, 20m, 2850m, 570m, null)], 2850m, 570m, 3420m),
            [], 0m, null, AssistantHarness.UserId, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
    }
}
