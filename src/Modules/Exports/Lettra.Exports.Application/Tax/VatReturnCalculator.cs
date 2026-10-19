using Lettra.Calculations;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Tax;

/// <summary>Ce que le brouillon sait de la période en plus des opérations : le journal, les brouillons, les réglages.</summary>
public sealed record VatReturnContext(
    VatReturnFormDto Form,
    VatDeclarationRegimeDto? Regime,
    DateOnly From,
    DateOnly To,
    DateOnly DueDate,
    decimal JournalVat,
    IReadOnlyList<string> Drafts,
    IReadOnlyList<string> NotTransmitted,
    bool VatOnReceipts,
    IReadOnlyList<VatInstallmentResponse> Installments,
    DateTimeOffset? FiledAt);

/// <summary>
/// Brouillon de CA3 ou de CA12, partie ventes : chaque opération va dans une case selon sa catégorie EN 16931 et son
/// taux, chaque case garde les pièces qui la composent. Fonction pure.
/// <para>
/// Correspondance <b>à faire relire par l'expert-comptable</b> (proposition §13) : taxable (S, Z) en A1 et dans la ligne
/// de son taux (08 à 20 %, 9B à 10 %, 09 à 5,5 %) ; exportations (G) en E1 ; exonérations (E), autoliquidation BTP et
/// prestations de services intracommunautaires (AE) en E2 ; livraisons intracommunautaires (K) en F2. La franchise et
/// les opérations hors champ (O) ainsi que les débours ne sont déclarés nulle part : ils restent visibles à part.
/// Les numéros de ligne sont ceux de la 3310-CA3-SD ; ceux de la 3517-S-SD (CA12) restent à reporter à la relecture.
/// </para>
/// </summary>
public static class VatReturnCalculator
{
    public const string SectionTaxable = "Taxable";
    public const string SectionNonTaxable = "NonTaxable";
    public const string SectionGross = "Gross";
    public const string SectionTotals = "Totals";
    public const string SectionExcluded = "Excluded";

    public const string CheckJournal = "JournalMatch";
    public const string CheckDrafts = "Drafts";
    public const string CheckNotTransmitted = "NotTransmitted";
    public const string CheckVatOnReceipts = "VatOnReceipts";
    public const string CheckOtherRate = "OtherRate";
    public const string CheckDeductible = "DeductibleMissing";

    /// <summary>Les taux de la métropole qui ont leur ligne dans la CA3, du plus fort au plus faible.</summary>
    private static readonly (decimal Rate, string Key, string? Line)[] Rates =
    [
        (20m, "Rate20", "08"),
        (10m, "Rate10", "9B"),
        (5.5m, "Rate55", "09"),
        (2.1m, "Rate21", null),
    ];

    public static VatReturnResponse Build(IReadOnlyList<VatOperation> operations, VatReturnContext context)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(context);
        bool ca3 = context.Form == VatReturnFormDto.Ca3;
        string? Line(string line) => ca3 ? line : null;

        List<VatOperation> declared = [.. operations.Where(o => !IsExcluded(o))];
        List<VatOperation> taxed = [.. declared.Where(o => VatCategoryCodes.IsTaxed(o.Category))];
        List<VatReturnBoxResponse> boxes =
        [
            Box("A1", SectionTaxable, Line("A1"), null, taxed, withVat: false),
            Box("E1", SectionNonTaxable, Line("E1"), null, declared.Where(o => o.Category == VatCategoryCodes.Export), withVat: false),
            Box("E2", SectionNonTaxable, Line("E2"), null,
                declared.Where(o => o.Category is VatCategoryCodes.Exempt or VatCategoryCodes.ReverseCharge), withVat: false),
            Box("F2", SectionNonTaxable, Line("F2"), null, declared.Where(o => o.Category == VatCategoryCodes.IntraCommunity), withVat: false),
        ];

        foreach ((decimal rate, string key, string? line) in Rates)
        {
            List<VatOperation> atRate = [.. taxed.Where(o => o.RatePercent == rate)];
            if (rate >= 5.5m || atRate.Count > 0)
            {
                boxes.Add(Box(key, SectionGross, line is null ? null : Line(line), rate, atRate, withVat: true));
            }
        }

        List<VatOperation> otherRates = [.. taxed.Where(o => !Rates.Any(r => r.Rate == o.RatePercent))];
        foreach (IGrouping<decimal, VatOperation> other in otherRates.GroupBy(o => o.RatePercent).OrderByDescending(g => g.Key))
        {
            boxes.Add(Box("RateOther", SectionGross, null, other.Key, other, withVat: true));
        }

        decimal gross = taxed.Sum(o => o.VatAmount);
        const decimal deductible = 0m;
        decimal net = gross - deductible;
        boxes.Add(Total("GrossTotal", Line("16"), gross));
        boxes.Add(Total("DeductibleTotal", Line("23"), deductible));
        boxes.Add(Total("Credit", Line("25"), net < 0m ? -net : 0m));
        boxes.Add(Total("NetDue", Line("28"), net > 0m ? net : 0m));
        boxes.Add(Box("OutOfScope", SectionExcluded, null, null, operations.Where(IsExcluded), withVat: false));

        List<TaxCheckResponse> checks =
        [
            gross == context.JournalVat
                ? new TaxCheckResponse(CheckJournal, TaxCheckSeverityDto.Ok, context.JournalVat, gross)
                : new TaxCheckResponse(CheckJournal, TaxCheckSeverityDto.Blocking, context.JournalVat, gross),
            context.Drafts.Count == 0
                ? new TaxCheckResponse(CheckDrafts, TaxCheckSeverityDto.Ok, Count: 0)
                : new TaxCheckResponse(CheckDrafts, TaxCheckSeverityDto.Warning, Count: context.Drafts.Count, Pieces: context.Drafts),
            context.NotTransmitted.Count == 0
                ? new TaxCheckResponse(CheckNotTransmitted, TaxCheckSeverityDto.Ok, Count: 0)
                : new TaxCheckResponse(CheckNotTransmitted, TaxCheckSeverityDto.Warning, Count: context.NotTransmitted.Count, Pieces: context.NotTransmitted),
        ];

        List<string> dueOnReceipt = [.. operations.Where(o => o.DueOnReceipt && o.VatAmount != 0m).Select(o => o.PieceNumber).Distinct()];
        if (!context.VatOnReceipts && dueOnReceipt.Count > 0)
        {
            checks.Add(new TaxCheckResponse(CheckVatOnReceipts, TaxCheckSeverityDto.Warning, Count: dueOnReceipt.Count, Pieces: dueOnReceipt));
        }

        if (otherRates.Count > 0)
        {
            checks.Add(new TaxCheckResponse(CheckOtherRate, TaxCheckSeverityDto.Warning, Count: otherRates.Select(o => o.InvoiceId).Distinct().Count(),
                Pieces: [.. otherRates.Select(o => o.PieceNumber).Distinct()]));
        }

        checks.Add(new TaxCheckResponse(CheckDeductible, TaxCheckSeverityDto.Warning));

        return new VatReturnResponse(
            context.Form,
            context.Regime,
            context.From,
            context.To,
            context.DueDate,
            boxes,
            gross,
            deductible,
            net > 0m ? net : 0m,
            net < 0m ? -net : 0m,
            checks,
            context.Installments,
            context.VatOnReceipts,
            context.FiledAt);
    }

    /// <summary>Franchise, hors champ et débours : pas d'opération imposable ni exonérée à déclarer.</summary>
    public static bool IsExcluded(VatOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Category == VatCategoryCodes.OutOfScope || operation.ExemptionCode == VatOperations.DisbursementExemption;
    }

    /// <summary>Les pièces d'une case, une ligne par pièce, de la plus ancienne à la plus récente.</summary>
    public static IReadOnlyList<TaxSourceResponse> Sources(IEnumerable<VatOperation> operations) =>
        operations
            .GroupBy(o => (o.InvoiceId, o.Date))
            .Select(g =>
            {
                VatOperation first = g.First();
                return new TaxSourceResponse(
                    first.InvoiceId, (InvoiceKindDto)(int)first.Kind, first.PieceNumber, first.Date, first.CustomerId, first.CustomerName,
                    g.Sum(o => o.BaseExclTax), g.Sum(o => o.VatAmount));
            })
            .OrderBy(s => s.Date)
            .ThenBy(s => s.PieceNumber, StringComparer.Ordinal)
            .ToList();

    private static VatReturnBoxResponse Box(string key, string section, string? line, decimal? rate, IEnumerable<VatOperation> operations, bool withVat)
    {
        List<VatOperation> list = [.. operations];
        return new VatReturnBoxResponse(
            key, section, line, rate, list.Sum(o => o.BaseExclTax), withVat ? list.Sum(o => o.VatAmount) : null, Sources(list));
    }

    private static VatReturnBoxResponse Total(string key, string? line, decimal amount) =>
        new(key, SectionTotals, line, null, null, amount, []);
}
