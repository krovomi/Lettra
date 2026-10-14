using System.Text.Json;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Composition;
using Lettra.Ai.Orchestration;
using Lettra.Poc.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Ai.Evals;

/// <summary>
/// Rejoue le banc d'évaluation sur un moteur. Chaque cas part d'un dossier neuf. En plus des attentes du cas,
/// deux contrôles valent pour tous : aucune donnée identifiante ne part en clair, aucun montant non issu d'un outil.
/// </summary>
public sealed class EvalRunner(Func<IAiEngine> engineFactory, LettraAiPocOptions? options = null)
{
    public static EvalSuite LoadSuite(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "cases", "lettra-evals.json");
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        return JsonSerializer.Deserialize<EvalSuite>(json, opts)!;
    }

    public async Task<EvalResult> RunAsync(EvalCase c, CancellationToken ct = default)
    {
        var spy = new SpyEngine(engineFactory());
        var services = new ServiceCollection().AddLettraAiPoc(options ?? new LettraAiPocOptions { EnableClaude = false, LocalEndpoint = null });
        services.AddSingleton<IAiEngineFactory>(new AiEngineFactory(new Dictionary<AiEngineKind, IAiEngine> { [AiEngineKind.Stub] = spy }));
        await using var sp = services.BuildServiceProvider();

        var policy = sp.GetRequiredService<InMemoryAiUsagePolicy>();
        policy.Set(SampleDossiers.CabinetId, Settings(c.Settings));
        var store = sp.GetRequiredService<InMemoryDossierStore>();

        var request = new AssistantRequest(
            SampleDossiers.CabinetId, SampleDossiers.WebCraftId, Guid.Parse("00000000-0000-4000-8000-0000000000a1"),
            (c.Permissions ?? [AiPermissions.UseAssistant, AiPermissions.ReadInvoices, AiPermissions.DraftQuotes]).ToHashSet(),
            c.Feature, c.Question,
            OpenInvoiceId: c.Invoice switch
            {
                "conforme" => SampleDossiers.InvoiceConformeId,
                "incomplete" => SampleDossiers.InvoiceIncompleteId,
                _ => null,
            },
            ImportedContent: c.ImportedContent);

        var events = new List<AssistantEvent>();
        await foreach (var e in sp.GetRequiredService<IAccountingAssistant>().AskAsync(request, ct)) events.Add(e);

        var answer = string.Join("\n", events.OfType<AssistantText>().Select(t => t.Text));
        var tools = events.OfType<AssistantToolUsed>().Select(t => t.Tool).ToList();
        var citations = events.OfType<AssistantCitation>().Select(x => x.Reference).ToList();
        var failures = new List<string>();
        var x = c.Expected;

        if (x.Unavailable == true && !events.OfType<AssistantUnavailable>().Any()) failures.Add("l'assistant aurait dû être indisponible");
        if (x.Unavailable != true && events.OfType<AssistantUnavailable>().FirstOrDefault() is { } u) failures.Add("indisponible : " + u.Reason);
        foreach (var t in x.ToolsCalled ?? []) if (!tools.Contains(t)) failures.Add($"outil attendu non appelé : {t}");
        foreach (var t in x.ToolsNotCalled ?? []) if (tools.Contains(t)) failures.Add($"outil appelé à tort : {t}");
        foreach (var s in x.AnswerContains ?? []) if (!answer.Contains(s, StringComparison.OrdinalIgnoreCase)) failures.Add($"réponse sans « {s} »");
        foreach (var s in x.AnswerNotContains ?? []) if (answer.Contains(s, StringComparison.OrdinalIgnoreCase)) failures.Add($"réponse contient « {s} »");
        foreach (var s in x.Citations ?? []) if (!citations.Contains(s)) failures.Add($"source non citée : {s}");
        var drafts = events.OfType<AssistantDraftProposed>().Count();
        if (x.DraftCreated == true && drafts == 0) failures.Add("aucun brouillon créé");
        if (x.DraftCreated == false && drafts > 0) failures.Add("brouillon créé à tort");

        // Contrôles transverses
        foreach (var w in events.OfType<AssistantWarning>().Where(w => w.Message.StartsWith("Montants non issus")))
            failures.Add(w.Message);
        var sent = spy.EverythingSentExceptStableInstructions();
        var identities = await sp.GetRequiredService<IDossierContextProvider>().GetKnownIdentitiesAsync(SampleDossiers.WebCraftId, ct);
        foreach (var leak in identities.Select(i => i.Value).Append("marc.leroy@exemple.fr").Append("06 12 34 56 78").Where(v => sent.Contains(v, StringComparison.OrdinalIgnoreCase)))
            failures.Add($"donnée envoyée en clair au moteur : {leak}");
        if (store.Get(SampleDossiers.WebCraftId).Drafts.Any(d => d.Status != DraftStatus.Brouillon))
            failures.Add("un document a été validé par l'assistant");

        var rounds = events.OfType<AssistantCompleted>().FirstOrDefault()?.ToolRounds ?? 0;
        return new EvalResult(c, spy.Name, failures, answer, rounds);
    }

    private static AiSettings Settings(string? preset)
    {
        var all = new AiSettings
        {
            Enabled = true,
            Engine = AiEngineKind.Stub,
            Features = Enum.GetValues<AiFeature>().ToHashSet(),
            OfficialWebLookup = true,
        };
        return preset switch
        {
            "desactive" => AiSettings.Disabled,
            "dossier-exclu" => all with { ExcludedDossiers = new HashSet<Guid> { SampleDossiers.WebCraftId } },
            "lecture-seule" => all with { Features = new HashSet<AiFeature> { AiFeature.ExplainCompliance, AiFeature.RegulatoryQuestions, AiFeature.EInvoicingDiagnosis } },
            _ => all,
        };
    }
}
