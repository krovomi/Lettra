using Lettra.Compliance.Invoices;
using Xunit.Abstractions;

namespace Lettra.Compliance.Tests;

/// <summary>
/// Totaux de facture comparés à computeTotals (generate-facturx.js). Lettra calcule en décimal exact :
/// chaque écart avec la référence doit correspondre à un écart connu et expliqué, jamais à une erreur de Lettra.
/// </summary>
public sealed class InvoiceTotalsParityTests(ITestOutputHelper output)
{
    private enum Outcome { Identique, ArrondiFlottantReference, TvaSurHtArrondi, TauxZeroReference, Inexplique }

    [Fact]
    public void Chaque_ecart_avec_la_reference_est_un_ecart_connu()
    {
        var cases = ParityFixtures.LoadCases("totals.json");
        var counts = Enum.GetValues<Outcome>().ToDictionary(o => o, _ => 0);
        var unexplained = new List<string>();

        foreach (var c in cases)
        {
            var company = PaperasseJson.ReadCompany(c!["company"]!.ToJsonString());
            var invoice = PaperasseJson.ReadInvoice(c["invoice"]!.ToJsonString());
            var e = c["expected"]!;
            var reference = (Ht: e["totalHT"]!.GetValue<string>(), Tva: e["totalTVA"]!.GetValue<string>(), Ttc: e["totalTTC"]!.GetValue<string>());

            var lettra = InvoiceTotalsCalculator.Compute(company, invoice);
            Assert.Equal(lettra.TotalExcludingVat + lettra.TotalVat, lettra.TotalIncludingVat); // toujours cohérent

            var outcome = Classify(company, invoice, lettra, reference);
            counts[outcome]++;
            if (outcome == Outcome.Inexplique)
                unexplained.Add($"{c["name"]} : référence {reference}, Lettra ({F(lettra.TotalExcludingVat)}, {F(lettra.TotalVat)}, {F(lettra.TotalIncludingVat)})");
        }

        foreach (var (k, v) in counts) output.WriteLine($"{k} : {v}");
        Assert.True(counts[Outcome.Identique] > cases.Count / 2, "la majorité des cas doit être identique au centime");
        Assert.True(unexplained.Count == 0, string.Join("\n", unexplained.Take(5)));
    }

    private static Outcome Classify(CompanyProfile company, InvoiceDocument invoice, InvoiceTotals lettra, (string Ht, string Tva, string Ttc) reference)
    {
        if ((F(lettra.TotalExcludingVat), F(lettra.TotalVat), F(lettra.TotalIncludingVat)) == reference) return Outcome.Identique;
        if (!company.IsVatFranchise && company.Tax?.TvaRate == 0m) return Outcome.TauxZeroReference;

        // Recalcul de la formule de la référence (sans arrondi intermédiaire), mais en décimal exact.
        var rate = company.IsVatFranchise ? 0m : company.Tax?.TvaRate ?? 0.20m;
        var ht = invoice.Lines!.Sum(l => l.Quantity!.Value * l.UnitPrice!.Value);
        var exact = (Ht: F(R(ht)), Tva: F(R(ht * rate)), Ttc: F(R(ht + ht * rate)));

        // La référence diffère du calcul exact : seul un demi-centime mal arrondi par toFixed sur un flottant l'explique.
        if (exact != reference)
        {
            var midpoints = new[] { ht, ht * rate, ht + ht * rate }.Any(v => (v * 100m) % 1m == 0.5m);
            return midpoints ? Outcome.ArrondiFlottantReference : Outcome.Inexplique;
        }

        // Référence exacte : l'écart vient de la règle Lettra (TVA calculée sur le HT arrondi au centime).
        return ht != R(ht) ? Outcome.TvaSurHtArrondi : Outcome.Inexplique;
    }

    private static decimal R(decimal v) => InvoiceTotalsCalculator.RoundCents(v);
    private static string F(decimal v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
