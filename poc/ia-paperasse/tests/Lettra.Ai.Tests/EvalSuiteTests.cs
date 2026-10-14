using Lettra.Ai.Evals;
using Lettra.Ai.Stub;
using Xunit.Abstractions;

namespace Lettra.Ai.Tests;

/// <summary>Le banc d'évals passe à 100 % sur le moteur simulé (cas bloquants, de sécurité et de qualité).</summary>
public sealed class EvalSuiteTests(ITestOutputHelper output)
{
    public static TheoryData<string> CaseIds() => new(EvalRunner.LoadSuite().Cases.Select(c => c.Id));

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Cas_du_banc_d_evals(string id)
    {
        var c = EvalRunner.LoadSuite().Cases.Single(x => x.Id == id);
        var result = await new EvalRunner(() => new ScriptedAccountantEngine()).RunAsync(c);
        output.WriteLine(result.Answer);
        Assert.True(result.Passed, string.Join("\n", result.Failures));
    }
}
