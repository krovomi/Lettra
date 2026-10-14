using System.Text.Json.Nodes;
using Lettra.Ai.Tools.Calculations;

namespace Lettra.Ai.Tests;

/// <summary>
/// Parité des calculs (CCA, amortissement, IS, acomptes TVA, prorata) avec calc.js de Paperasse @ f2e15c4.
/// Les résultats de référence viennent du banc du POC (poc/ia-paperasse/tests/parity/harness.js).
/// </summary>
public sealed class CalculatorParityTests
{
    [Fact]
    public void Le_port_csharp_produit_la_meme_sortie_que_calc_js_sur_tous_les_cas()
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Parity", "calc.json")))!;
        root["reference"]!.GetValue<string>().Should().Be("romainsimon/paperasse@f2e15c4");
        JsonArray cases = root["cases"]!.AsArray();
        List<string> mismatches = [];

        foreach (JsonNode? c in cases)
        {
            Dictionary<string, string> args = c!["args"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>(), StringComparer.Ordinal);
            CalcResult result = AccountingCalculator.Run(c["command"]!.GetValue<string>(), args);
            JsonNode expected = c["expected"]!;
            bool success = expected["exitCode"]!.GetValue<int>() == 0;
            (string expectedText, string actualText) = success
                ? (expected["stdout"]!.GetValue<string>(), result.IsSuccess ? result.ToText().TrimEnd('\n') : result.ToText())
                : (expected["stderr"]!.GetValue<string>(), result.ToText());

            if (expectedText != actualText || success != result.IsSuccess)
            {
                mismatches.Add($"{c["name"]} {c["command"]} {c["args"]!.ToJsonString()}\n--- attendu\n{expectedText}\n--- obtenu\n{actualText}");
            }
        }

        cases.Count.Should().BeGreaterThanOrEqualTo(1000);
        mismatches.Should().BeEmpty(string.Join('\n', mismatches.Take(5)));
    }

    [Theory]
    [InlineData("cca", "total=1200;jours-n-plus-1=92;jours-totaux=365", "CCA", "302,47 EUR")]
    [InlineData("amortissement-lineaire", "valeur=3000;duree=3;jours-utilises=200;base-jours=365", "Dotation periode", "547,95 EUR")]
    [InlineData("is", "resultat-fiscal=10000;taux=25", "IS total", "2500,00 EUR")]
    [InlineData("is", "resultat-fiscal=50000;taux-reduit=15;taux-normal=25;jours-exercice=182", "IS total", "10380,83 EUR")]
    [InlineData("tva-acomptes-rs", "tva-n-1=12000", "Total acomptes", "11400,00 EUR")]
    [InlineData("prorata", "montant=1000;jours=50;base=365", "Resultat", "136,99 EUR")]
    public void Reprend_les_cas_de_test_de_Paperasse(string command, string args, string row, string expected)
    {
        Dictionary<string, string> parsed = args.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);

        CalcResult result = AccountingCalculator.Run(command, parsed);

        result.IsSuccess.Should().BeTrue(result.Error);
        result[row].Should().Be(expected);
    }
}
