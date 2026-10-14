using System.Text.Json;
using System.Text.Json.Nodes;
using Lettra.Compliance.Invoices;
using Lettra.Compliance.Mentions;
using Xunit.Abstractions;

namespace Lettra.Compliance.Tests;

/// <summary>Parité du contrôle des mentions obligatoires avec validate-facture.js (Paperasse).</summary>
public sealed class MandatoryMentionsParityTests(ITestOutputHelper output)
{
    [Fact]
    public void Le_port_csharp_donne_le_meme_verdict_que_validate_facture_js_sur_tous_les_cas()
    {
        var cases = ParityFixtures.LoadCases("validate-facture.json");
        var validator = new InvoiceComplianceValidator();
        var mismatches = new List<string>();
        int blocked = 0, warned = 0;

        foreach (var c in cases)
        {
            var company = PaperasseJson.ReadCompany(c!["company"]!.ToJsonString());
            var invoice = PaperasseJson.ReadInvoice(c["invoice"]!.ToJsonString());
            var today = DateOnly.Parse(c["today"]!.GetValue<string>());
            var strict = c["strict"]!.GetValue<bool>();

            var report = validator.Validate(company, invoice, today, strict);
            var actual = JsonSerializer.SerializeToNode(report)!;
            if (report.BlocksValidation) blocked++;
            if (report.Warnings.Count > 0) warned++;

            if (!JsonNode.DeepEquals(actual, c["expected"]))
                mismatches.Add($"{c["name"]}\n  attendu : {c["expected"]!.ToJsonString()}\n  obtenu  : {actual.ToJsonString()}");
        }

        output.WriteLine($"{cases.Count} factures comparées, {blocked} bloquées, {warned} avec avertissements, {mismatches.Count} écarts.");
        Assert.True(cases.Count >= 1000, "le banc doit couvrir au moins 1 000 factures");
        Assert.True(blocked > 100 && blocked < cases.Count - 50, "le banc doit mêler factures conformes et non conformes");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(5)));
    }
}
