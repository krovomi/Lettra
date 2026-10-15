using Lettra.Calculations;
using Lettra.Compliance.Invoices;

namespace Lettra.Compliance.Tests;

/// <summary>L'outil de vérification du POC donne les mêmes totaux que le produit (Lettra.Calculations).</summary>
public sealed class InvoiceTotalsProductAlignmentTests
{
    [Fact]
    public void Chaque_ligne_est_arrondie_avant_la_somme_comme_dans_le_produit()
    {
        var company = PaperasseJson.ReadCompany("""{"name":"Société","tax":{"tva_rate":0.2}}""");
        var invoice = PaperasseJson.ReadInvoice("""
            {"lines":[{"quantity":3,"unit_price":19.155},{"quantity":3,"unit_price":19.155},{"quantity":1,"unit_price":28.2}]}
            """);

        var poc = InvoiceTotalsCalculator.Compute(company, invoice);
        var product = DocumentCalculator.Compute([new(3m, 19.155m, 20m), new(3m, 19.155m, 20m), new(1m, 28.2m, 20m)]);

        Assert.Equal([57.47m, 57.47m, 28.20m], poc.LineTotals);
        Assert.Equal(143.14m, poc.TotalExcludingVat); // 143,13 si l'on additionne les lignes non arrondies
        Assert.Equal((product.TotalExclTax, product.TotalVat, product.TotalInclTax), (poc.TotalExcludingVat, poc.TotalVat, poc.TotalIncludingVat));
    }
}
