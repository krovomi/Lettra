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

/// <summary>Une ligne HT de la pièce vue par la comptabilité.</summary>
public readonly record struct RevenueLine(RevenueNature Nature, decimal TotalExclTax);

/// <summary>Une ligne d'écriture : un compte, un sens, un montant positif. <paramref name="VatRatePercent"/> pour 44571.</summary>
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
}

/// <summary>
/// Écriture de vente d'une pièce validée (journal des ventes), calculée de façon pure : le client au débit du TTC,
/// les produits (706, 707) ou les acomptes (4191) au crédit du HT par nature, la TVA collectée (44571) au crédit par
/// taux. Un avoir inverse les sens ; un montant négatif (acompte déduit d'une facture finale) passe au sens opposé.
/// L'écriture est toujours équilibrée : c'est ce que le FEC (phase 5) exporte.
/// </summary>
public static class SalesEntryCalculator
{
    public static IReadOnlyList<AccountingEntryLine> Compute(
        AccountingDocumentKind kind,
        IEnumerable<RevenueLine> lines,
        DocumentTotals totals)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(totals);

        // Montants au crédit pour une facture (le client est au débit).
        List<(string Account, decimal Amount, decimal? Rate)> credits = lines
            .GroupBy(l => AccountOf(l.Nature))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Sum(l => l.TotalExclTax), (decimal?)null))
            .Where(c => c.Item2 != 0m)
            .ToList();
        credits.AddRange(totals.Breakdown
            .Where(b => b.VatAmount != 0m)
            .Select(b => (SalesAccounts.VatCollected, b.VatAmount, (decimal?)b.VatRatePercent)));

        decimal revenue = credits.Sum(c => c.Amount);
        if (revenue != totals.TotalInclTax)
        {
            throw new InvalidOperationException("Les lignes ne redonnent pas le total TTC de la pièce : écriture déséquilibrée.");
        }

        List<AccountingEntryLine> entry = [Side(SalesAccounts.Customers, totals.TotalInclTax, debit: true, rate: null)];
        entry.AddRange(credits.Select(c => Side(c.Account, c.Amount, debit: false, c.Rate)));

        return kind == AccountingDocumentKind.CreditNote
            ? entry.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToList()
            : entry;
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
