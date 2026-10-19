namespace Lettra.Calculations;

/// <summary>Rôle comptable d'une ligne de pièce : il choisit le compte de produit.</summary>
public enum RevenueNature
{
    /// <summary>Vente de marchandises ou de produits : 707.</summary>
    Goods = 1,

    /// <summary>Prestation de services : 706.</summary>
    Services = 2,

    /// <summary>Acompte reçu (facture d'acompte, ou acompte déduit d'une facture finale) : 4191.</summary>
    DownPayment = 3,
}

/// <summary>Nature de la pièce comptabilisée.</summary>
public enum AccountingDocumentKind
{
    Invoice = 1,
    DownPayment = 2,

    /// <summary>Avoir : les sens de la facture sont inversés.</summary>
    CreditNote = 3,
}

/// <summary>
/// Une ligne HT de la pièce vue par la comptabilité. Le taux et la catégorie de TVA servent à séparer la TVA des
/// prestations de services, exigible à l'encaissement, de celle des biens (<see cref="SalesEntryCalculator"/>).
/// </summary>
public readonly record struct RevenueLine(
    RevenueNature Nature,
    decimal TotalExclTax,
    decimal VatRatePercent = 0m,
    string VatCategory = VatCategoryCodes.Standard);

/// <summary>Une ligne d'écriture : un compte, un sens, un montant positif. <paramref name="VatRatePercent"/> pour 44571 et 44574.</summary>
public sealed record AccountingEntryLine(string Account, decimal Debit, decimal Credit, decimal? VatRatePercent = null);

/// <summary>
/// Plan comptable général utilisé par les écritures de vente. À relire par l'expert-comptable (jalon J2) : un cabinet
/// pourra paramétrer ses comptes, la structure reste.
/// </summary>
public static class SalesAccounts
{
    public const string Customers = "411";
    public const string ServicesRevenue = "706";
    public const string GoodsRevenue = "707";
    public const string DownPaymentsReceived = "4191";
    public const string VatCollected = "44571";

    /// <summary>
    /// TVA collectée en attente d'exigibilité : celle des prestations de services sans option sur les débits, due à
    /// l'encaissement (art. 269 CGI). Elle passe en 44571 au fur et à mesure des règlements (journal de banque).
    /// </summary>
    public const string VatPending = "44574";
}

/// <summary>
/// Écriture de vente d'une pièce validée (journal des ventes), calculée de façon pure : le client au débit du TTC,
/// les produits (706, 707) ou les acomptes (4191) au crédit du HT par nature, la TVA collectée (44571) au crédit par
/// taux. Un avoir inverse les sens ; un montant négatif (acompte déduit d'une facture finale) passe au sens opposé.
/// L'écriture est toujours équilibrée : c'est ce que le FEC (phase 5) exporte.
/// <para>
/// TVA sur les encaissements (paramètre <c>servicesVatOnReceipt</c>, question Q12 de l'expert, vague 6) : pour une
/// pièce sans option sur les débits, la TVA des lignes de services d'un taux est calculée sur leur base et passe en
/// 44574 ; le reste de la TVA du taux (biens, acomptes) reste en 44571. Le total de TVA du taux, celui imprimé sur la
/// facture, ne change pas. Les écritures déjà enregistrées ne sont jamais recalculées.
/// </para>
/// </summary>
public static class SalesEntryCalculator
{
    public static IReadOnlyList<AccountingEntryLine> Compute(
        AccountingDocumentKind kind,
        IEnumerable<RevenueLine> lines,
        DocumentTotals totals,
        bool servicesVatOnReceipt = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(totals);
        List<RevenueLine> revenue = [.. lines];

        // Montants au crédit pour une facture (le client est au débit).
        List<(string Account, decimal Amount, decimal? Rate)> credits = revenue
            .GroupBy(l => AccountOf(l.Nature))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Sum(l => l.TotalExclTax), (decimal?)null))
            .Where(c => c.Item2 != 0m)
            .ToList();
        foreach (VatBreakdownLine b in totals.Breakdown.Where(b => b.VatAmount != 0m))
        {
            decimal pending = servicesVatOnReceipt ? ServicesVat(revenue, b) : 0m;
            if (b.VatAmount - pending != 0m)
            {
                credits.Add((SalesAccounts.VatCollected, b.VatAmount - pending, b.VatRatePercent));
            }

            if (pending != 0m)
            {
                credits.Add((SalesAccounts.VatPending, pending, b.VatRatePercent));
            }
        }

        if (credits.Sum(c => c.Amount) != totals.TotalInclTax)
        {
            throw new InvalidOperationException("Les lignes ne redonnent pas le total TTC de la pièce : écriture déséquilibrée.");
        }

        List<AccountingEntryLine> entry = [Side(SalesAccounts.Customers, totals.TotalInclTax, debit: true, rate: null)];
        entry.AddRange(credits.Select(c => Side(c.Account, c.Amount, debit: false, c.Rate)));

        return kind == AccountingDocumentKind.CreditNote
            ? entry.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToList()
            : entry;
    }

    /// <summary>TVA des seules lignes de services d'un taux, par la même règle d'arrondi que la ventilation.</summary>
    private static decimal ServicesVat(IEnumerable<RevenueLine> lines, VatBreakdownLine rate)
    {
        decimal servicesBase = lines
            .Where(l => l.Nature == RevenueNature.Services
                && string.Equals(l.VatCategory, rate.VatCategory, StringComparison.Ordinal)
                && (VatCategoryCodes.IsTaxed(l.VatCategory) ? l.VatRatePercent : 0m) == rate.VatRatePercent)
            .Sum(l => l.TotalExclTax);
        return Rounding.ToCents(servicesBase * rate.VatRatePercent / 100m);
    }

    private static string AccountOf(RevenueNature nature) => nature switch
    {
        RevenueNature.Goods => SalesAccounts.GoodsRevenue,
        RevenueNature.Services => SalesAccounts.ServicesRevenue,
        RevenueNature.DownPayment => SalesAccounts.DownPaymentsReceived,
        _ => throw new ArgumentOutOfRangeException(nameof(nature), nature, null),
    };

    private static AccountingEntryLine Side(string account, decimal amount, bool debit, decimal? rate)
    {
        // Un montant négatif change de sens : l'écriture ne porte que des montants positifs.
        bool isDebit = amount < 0m ? !debit : debit;
        decimal value = Math.Abs(amount);
        return isDebit ? new(account, value, 0m, rate) : new(account, 0m, value, rate);
    }
}
