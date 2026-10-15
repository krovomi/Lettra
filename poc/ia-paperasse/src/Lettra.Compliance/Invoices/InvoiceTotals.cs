using Lettra.Calculations;

namespace Lettra.Compliance.Invoices;

public sealed record VatBreakdown(decimal Rate, decimal TaxableAmount, decimal VatAmount);

public sealed record InvoiceTotals(
    IReadOnlyList<decimal> LineTotals,
    IReadOnlyList<VatBreakdown> ByRate,
    decimal TotalExcludingVat,
    decimal TotalVat,
    decimal TotalIncludingVat,
    bool IsVatFranchise);

/// <summary>
/// Totaux d'une facture en décimal exact, ventilés par taux (art. 242 nonies A, I-12° CGI).
/// Référence fonctionnelle : <c>computeTotals</c> de <c>generate-facturx.js</c> (Paperasse), qui calcule
/// en virgule flottante avec un seul taux. Les écarts attendus sont décrits dans <see cref="KnownDifferencesWithReference"/>.
/// </summary>
public static class InvoiceTotalsCalculator
{
    public const decimal DefaultVatRate = 0.20m;

    public static readonly IReadOnlyList<string> KnownDifferencesWithReference =
    [
        "Lignes : Lettra arrondit chaque ligne au centime avant de les additionner (Lettra.Calculations, comme le produit) ; la référence additionne les lignes non arrondies.",
        "Arrondi : Lettra arrondit au centime en décimal exact (demi-centime vers le haut) ; la référence arrondit des nombres flottants avec toFixed(2), ce qui peut perdre le demi-centime (5,005 → 5,00).",
        "Cohérence : Lettra calcule TTC = HT arrondi + TVA arrondie ; la référence arrondit séparément trois flottants, si bien que HT + TVA peut différer du TTC affiché d'un centime.",
        "Taux à 0 : la référence remplace un tva_rate de 0 par 20 % (« || 0.20 ») ; Lettra garde 0 %.",
        "Taux par ligne : Lettra accepte un taux par ligne (vat_rate) et ventile la TVA par taux ; la référence n'a qu'un taux par entreprise.",
    ];

    public static InvoiceTotals Compute(CompanyProfile company, InvoiceDocument invoice)
    {
        var franchise = company.IsVatFranchise;
        var companyRate = company.Tax?.TvaRate ?? DefaultVatRate;
        var lines = invoice.Lines ?? [];

        // Même calcul que le produit (Lettra.Calculations) : lignes arrondies, puis TVA par taux sur leur somme.
        var inputs = lines
            .Select(l => new LineInput(l.Quantity ?? 0m, l.UnitPrice ?? 0m, (franchise ? 0m : l.VatRate ?? companyRate) * 100m))
            .ToList();
        var totals = DocumentCalculator.Compute(inputs);
        var byRate = totals.Breakdown
            .Select(b => new VatBreakdown(b.VatRatePercent / 100m, b.BaseExclTax, b.VatAmount))
            .ToList();

        return new InvoiceTotals(
            [.. inputs.Select(LineAmounts.TotalExclTax)],
            byRate,
            totals.TotalExclTax,
            totals.TotalVat,
            totals.TotalInclTax,
            franchise);
    }

    public static decimal RoundCents(decimal amount) => Rounding.ToCents(amount);
}
