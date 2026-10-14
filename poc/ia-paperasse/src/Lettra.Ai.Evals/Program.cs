using System.Text;
using Lettra.Ai.Abstractions;
using Lettra.Ai.Claude;
using Lettra.Ai.Evals;
using Lettra.Ai.Local;
using Lettra.Ai.Stub;

// Usage : dotnet run --project src/Lettra.Ai.Evals -- [--engine stub|claude|local] [--report chemin.md] [--details]
var engine = Arg("--engine") ?? "stub";
var reportPath = Arg("--report");

Func<IAiEngine> factory = engine switch
{
    "stub" => () => new ScriptedAccountantEngine(),
    "claude" when !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) =>
        () => new ClaudeEngine(new Anthropic.AnthropicClient(), new ClaudeEngineOptions()),
    "local" when Environment.GetEnvironmentVariable("LETTRA_LOCAL_MODEL_URL") is { Length: > 0 } url =>
        () => new LocalModelEngine(new HttpClient(), new LocalModelOptions { Endpoint = new Uri(url), Model = Environment.GetEnvironmentVariable("LETTRA_LOCAL_MODEL") ?? "mistral-small" }),
    _ => throw new InvalidOperationException($"Moteur « {engine} » indisponible : clé ANTHROPIC_API_KEY ou LETTRA_LOCAL_MODEL_URL manquante."),
};

var suite = EvalRunner.LoadSuite();
var runner = new EvalRunner(factory);
var results = new List<EvalResult>();
foreach (var c in suite.Cases) results.Add(await runner.RunAsync(c));

var report = new StringBuilder();
report.AppendLine($"# Banc d'évals Lettra : moteur {results.FirstOrDefault()?.Engine ?? engine}").AppendLine();
report.AppendLine($"{results.Count(r => r.Passed)}/{results.Count} cas réussis ; bloquants et sécurité : " +
                  $"{results.Count(r => r.Case.IsBlocking && r.Passed)}/{results.Count(r => r.Case.IsBlocking)}.").AppendLine();
report.AppendLine("| Cas | Catégorie | Résultat | Étapes d'outils | Détail |").AppendLine("|---|---|---|---|---|");
foreach (var r in results)
    report.AppendLine($"| {r.Case.Id} | {r.Case.Category} | {(r.Passed ? "✅" : "❌")} | {r.ToolRounds} | {string.Join(" ; ", r.Failures).Replace("|", "/")} |");
if (args.Contains("--details"))
{
    report.AppendLine().AppendLine("## Réponses").AppendLine();
    foreach (var r in results)
        report.AppendLine($"### {r.Case.Id}").AppendLine().AppendLine($"Question : {r.Case.Question}").AppendLine()
              .AppendLine(string.IsNullOrWhiteSpace(r.Answer) ? "_(pas de réponse : assistant indisponible)_" : r.Answer).AppendLine();
}
Console.WriteLine(report);
if (reportPath is not null) File.WriteAllText(reportPath, report.ToString());

// Critère de passage (annexe B §8) : 100 % sur les cas bloquants et de sécurité.
return results.Where(r => r.Case.IsBlocking).All(r => r.Passed) ? 0 : 1;

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
