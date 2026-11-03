using System.Text.Json;
using Lettra.Ai.Orchestration;
using Lettra.Application.Abstractions.Ai;
using Lettra.Application.Diagnostics;
using Lettra.Contracts.Assistant;
using Lettra.Contracts.Common;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using static Lettra.Ai.Tests.ScriptedTurnsEngine;

namespace Lettra.Ai.Tests;

/// <summary>
/// Le moteur tombe en panne au milieu d'une réponse (réseau, délai, réponse illisible) : la réponse se termine sur
/// « moteur indisponible », la panne est au journal IA, et rien ne casse côté écran. Après des échecs répétés, Lettra
/// cesse d'appeler le moteur quelques minutes (§9 de la proposition « IA qui anticipe », annexe F §10).
/// </summary>
public sealed class EngineFallbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Une_panne_du_moteur_apres_un_outil_termine_la_reponse_sur_moteur_indisponible()
    {
        ScriptedTurnsEngine engine = new(
            _ => Call("calculer", new { operation = "prorata", parametres = new { montant = "1000", jours = "50" } }),
            _ => throw new HttpRequestException("connexion perdue"));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());
        using MetricCollector<long> fallbacks = new(LettraTelemetry.Ai.Fallbacks);

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "Prorata de 1000 € sur 50 jours ?");

        events.OfType<AssistantToolUsedEvent>().Should().ContainSingle(because: "les étapes déjà faites restent affichées");
        events.OfType<AssistantTextEvent>().Should().BeEmpty(because: "rien n'est inventé quand le moteur tombe");
        events.OfType<AssistantCompletedEvent>().Should().BeEmpty();
        events[^1].Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EngineFailed));
        (await h.AuditAsync()).Should().ContainSingle(e => e.Kind == AiAuditKind.EngineFailed)
            .Which.Should().Match<AiAuditEntry>(e => e.Detail == "moteur injoignable" && e.Engine == "test:script");
        fallbacks.GetMeasurementSnapshot().Should().Contain(m =>
            Equals(m.Tags[LettraTelemetry.Attributes.AiEngine], "claude_eu") && Equals(m.Tags[LettraTelemetry.Attributes.AiFallbackReason], "unreachable"));
    }

    public static TheoryData<Exception, string> Pannes => new()
    {
        { new TaskCanceledException("délai du client HTTP", new TimeoutException()), "délai dépassé" },
        { new TimeoutException(), "délai dépassé" },
        { new JsonException("jeton inattendu"), "réponse illisible" },
        { new AiEngineException(AiEngineFailure.Unreadable, "pas une réponse « chat completions »"), "réponse illisible" },
        { new InvalidOperationException("erreur du fournisseur", new HttpRequestException()), "moteur injoignable" },
    };

    [Theory]
    [MemberData(nameof(Pannes))]
    public async Task Chaque_panne_est_nommee_au_journal_sans_detail_technique(Exception panne, string raison)
    {
        ScriptedTurnsEngine engine = new(_ => throw panne);
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        List<AssistantEvent> events = await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");

        events.Should().ContainSingle().Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EngineFailed));
        AiAuditEntry entry = (await h.AuditAsync()).Single(e => e.Kind == AiAuditKind.EngineFailed);
        entry.Detail.Should().Be(raison);
        entry.Detail.Should().NotContain(panne.Message, because: "le message d'un fournisseur pourrait citer la demande");
        h.Logs.GetSnapshot().Should().Contain(r => r.Message.Contains("réponse terminée sans IA", StringComparison.Ordinal))
            .And.NotContain(r => r.Message.Contains(panne.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task L_arret_demande_par_l_utilisateur_n_est_pas_une_panne()
    {
        using CancellationTokenSource stop = new();
        ScriptedTurnsEngine engine = new(_ =>
        {
            stop.Cancel();
            throw new OperationCanceledException(stop.Token);
        });
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        Func<Task> ask = () => h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?", cancellationToken: stop.Token);

        await ask.Should().ThrowAsync<OperationCanceledException>();
        (await h.AuditAsync()).Should().NotContain(e => e.Kind == AiAuditKind.EngineFailed);
        h.Get<IAiEngineHealth>().IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue();
    }

    [Fact]
    public async Task Apres_des_echecs_repetes_Lettra_cesse_d_appeler_le_moteur_puis_reessaie_apres_la_pause()
    {
        bool down = true;
        ScriptedTurnsEngine engine = new(_ => down ? throw new HttpRequestException("503") : Say("Le taux normal est de 20 %."));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled(), now: Now);
        IAiEngineHealth health = h.Get<IAiEngineHealth>();

        for (int i = 0; i < 3; i++)
        {
            (await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?"))[^1].Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EngineFailed));
        }

        engine.Calls.Should().Be(3);
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeFalse();

        // Pendant la pause : réponse immédiate, sans appel au moteur, et le journal dit pourquoi.
        (await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?")).Should().ContainSingle()
            .Which.Should().Be(new AssistantUnavailableEvent(ErrorCodes.Ai.EnginePaused));
        engine.Calls.Should().Be(3);
        (await h.AuditAsync()).Should().Contain(e => e.Kind == AiAuditKind.EngineFailed && e.Detail == "moteur en pause après des échecs répétés");

        // Après la pause (deux minutes par défaut), l'appel suivant sert d'essai ; s'il répond, tout repart.
        ((FixedTime)h.Get<TimeProvider>()).Advance(TimeSpan.FromMinutes(2));
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue();
        down = false;
        (await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?")).OfType<AssistantTextEvent>().Should().ContainSingle();
        engine.Calls.Should().Be(4);
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue();
    }

    [Fact]
    public async Task La_pause_et_le_seuil_se_reglent()
    {
        ScriptedTurnsEngine engine = new(_ => throw new HttpRequestException("503"));
        Dictionary<string, string?> settings = AssistantHarness.Enabled();
        settings["Lettra:Ai:Resilience:MinimumThroughput"] = "2";
        settings["Lettra:Ai:Resilience:BreakDuration"] = "00:10:00";
        await using AssistantHarness h = new(engine, settings, now: Now);
        IAiEngineHealth health = h.Get<IAiEngineHealth>();

        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue();
        await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?");
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeFalse();

        ((FixedTime)h.Get<TimeProvider>()).Advance(TimeSpan.FromMinutes(9));
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeFalse();
        ((FixedTime)h.Get<TimeProvider>()).Advance(TimeSpan.FromMinutes(1));
        health.IsResponding(AiEngineKind.ClaudeEu).Should().BeTrue();
    }

    [Fact]
    public async Task Le_texte_de_l_IA_porte_sa_marque_lisible_par_machine_et_son_moteur()
    {
        ScriptedTurnsEngine engine = new(_ => Say("Le taux normal est de 20 %."));
        await using AssistantHarness h = new(engine, AssistantHarness.Enabled());

        AssistantTextEvent text = (await h.AskAsync(AssistantFeature.RegulatoryQuestions, "TVA ?")).OfType<AssistantTextEvent>().Single();

        text.Engine.Should().Be("test:script");
        text.AiGenerated.Should().BeTrue();
        text.DigitalSourceType.Should().Be("http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia");
    }

    [Theory]
    [InlineData(AiEngineKind.ClaudeEu, "claude_eu")]
    [InlineData(AiEngineKind.Local, "local")]
    [InlineData(AiEngineKind.None, "none")]
    public void Le_nom_d_un_moteur_dans_les_metriques_est_fixe(AiEngineKind kind, string expected) =>
        AiEngineCircuits.MetricName(kind).Should().Be(expected);

    [Fact]
    public void Un_moteur_jamais_appele_est_repute_repondre() =>
        new AiEngineCircuits(Microsoft.Extensions.Options.Options.Create(new AiResilienceOptions()), TimeProvider.System)
            .IsResponding(AiEngineKind.Local).Should().BeTrue();
}
