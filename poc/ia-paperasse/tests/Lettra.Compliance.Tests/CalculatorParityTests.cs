using Lettra.Compliance.Calculations;
using Xunit.Abstractions;

namespace Lettra.Compliance.Tests;

/// <summary>Parité des calculs (CCA, amortissement, IS, acomptes TVA, prorata) avec calc.js (Paperasse).</summary>
public sealed class CalculatorParityTests(ITestOutputHelper output)
{
    [Fact]
    public void Le_port_csharp_produit_la_meme_sortie_que_calc_js_sur_tous_les_cas()
    {
        var cases = ParityFixtures.LoadCases("calc.json");
        var mismatches = new List<string>();
        var failures = 0;

        foreach (var c in cases)
        {
            var args = c!["args"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
            var result = AccountingCalculator.Run(c["command"]!.GetValue<string>(), args);
            var expected = c["expected"]!;
            var exitCode = expected["exitCode"]!.GetValue<int>();

            var (expectedText, actualText) = exitCode == 0
                ? (expected["stdout"]!.GetValue<string>(), result.IsSuccess ? result.ToText().TrimEnd('\n') : result.ToText())
                : (expected["stderr"]!.GetValue<string>(), result.ToText());
            if (!result.IsSuccess) failures++;

            if (expectedText != actualText || (exitCode == 0) != result.IsSuccess)
                mismatches.Add($"{c["name"]} {c["command"]} {c["args"]!.ToJsonString()}\n--- attendu\n{expectedText}\n--- obtenu\n{actualText}");
        }

        output.WriteLine($"{cases.Count} calculs comparés ({failures} refus de paramètres), {mismatches.Count} écarts.");
        Assert.True(cases.Count >= 1000);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(5)));
    }

    [Theory]
    [InlineData("cca", "total=1200;jours-n-plus-1=92;jours-totaux=365", "CCA", "302,47 EUR")]
    [InlineData("amortissement-lineaire", "valeur=3000;duree=3;jours-utilises=200;base-jours=365", "Dotation periode", "547,95 EUR")]
    [InlineData("is", "resultat-fiscal=10000;taux=25", "IS total", "2500,00 EUR")]
    [InlineData("is", "resultat-fiscal=1000;taux=1", "IS total", "10,00 EUR")]
    [InlineData("is", "resultat-fiscal=50000;taux-reduit=15;taux-normal=25;jours-exercice=182", "IS total", "10380,83 EUR")]
    [InlineData("tva-acomptes-rs", "tva-n-1=12000", "Total acomptes", "11400,00 EUR")]
    [InlineData("prorata", "montant=1000;jours=50;base=365", "Resultat", "136,99 EUR")]
    public void Reprend_les_cas_de_test_deterministic_calculations_js(string command, string args, string row, string expected)
    {
        var parsed = args.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
        var result = AccountingCalculator.Run(command, parsed);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result[row]);
    }
}
