namespace Lettra.Calculations;

/// <summary>
/// Comptes du journal de banque utilisés par les encaissements (vague 6). À relire par l'expert-comptable (jalon J2) :
/// un cabinet pourra paramétrer ses comptes (512 par banque, par exemple), la structure reste.
/// </summary>
public static class PaymentAccounts
{
    public const string Bank = "512";

    /// <summary>Écart de règlement en faveur du client (il a payé un peu moins) : charge.</summary>
    public const string WriteOffLoss = "658";

    /// <summary>Écart de règlement en faveur de l'entreprise (le client a payé un peu plus) : produit.</summary>
    public const string WriteOffGain = "758";

    /// <summary>Code du journal de banque dans le FEC et la consultation.</summary>
    public const string JournalCode = "BQ";
}

/// <summary>Part de TVA en attente (44574) qui devient exigible (44571) à l'encaissement, pour un taux.</summary>
public readonly record struct VatTransfer(decimal VatRatePercent, decimal Amount);

/// <summary>
/// Écriture d'un encaissement (journal de banque), calculée de façon pure :
/// <list type="bullet">
/// <item>la banque (512) au débit du montant reçu ;</item>
/// <item>le client (411) au crédit du total imputé sur ses factures ;</item>
/// <item>l'écart de règlement : 658 au débit s'il a payé moins que l'imputé, 758 au crédit s'il a payé plus ;</item>
/// <item>la TVA sur les encaissements : 44574 au débit, 44571 au crédit, par taux.</item>
/// </list>
/// La contre-passation inverse tous les sens. L'écriture est toujours équilibrée.
/// </summary>
public static class PaymentEntryCalculator
{
    public static IReadOnlyList<AccountingEntryLine> Compute(decimal amountReceived, decimal allocatedToInvoices, IEnumerable<VatTransfer> transfers, bool reversal = false)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        if (amountReceived <= 0m || allocatedToInvoices <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amountReceived), "Un encaissement porte un montant reçu et un montant imputé positifs.");
        }

        List<AccountingEntryLine> entry = [new(PaymentAccounts.Bank, amountReceived, 0m)];
        decimal writeOff = allocatedToInvoices - amountReceived;
        if (writeOff > 0m)
        {
            entry.Add(new(PaymentAccounts.WriteOffLoss, writeOff, 0m));
        }

        entry.Add(new(SalesAccounts.Customers, 0m, allocatedToInvoices));
        if (writeOff < 0m)
        {
            entry.Add(new(PaymentAccounts.WriteOffGain, 0m, -writeOff));
        }

        foreach (VatTransfer transfer in transfers
            .Where(t => t.Amount != 0m)
            .GroupBy(t => t.VatRatePercent)
            .Select(g => new VatTransfer(g.Key, g.Sum(t => t.Amount)))
            .Where(t => t.Amount != 0m)
            .OrderByDescending(t => t.VatRatePercent))
        {
            // Une part négative (cas d'un avoir plus fort que la facture) passe au sens opposé.
            decimal amount = Math.Abs(transfer.Amount);
            bool forward = transfer.Amount > 0m;
            entry.Add(new(SalesAccounts.VatPending, forward ? amount : 0m, forward ? 0m : amount, transfer.VatRatePercent));
            entry.Add(new(SalesAccounts.VatCollected, forward ? 0m : amount, forward ? amount : 0m, transfer.VatRatePercent));
        }

        return reversal
            ? entry.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToList()
            : entry;
    }
}

/// <summary>
/// TVA sur les encaissements : la part de la TVA en attente d'une facture qui devient exigible avec un règlement.
/// Prorata du TTC encore dû, arrondi au centime par taux ; le règlement qui solde la facture prend tout le reste, pour
/// qu'aucun centime ne reste en 44574.
/// </summary>
public static class VatOnReceipt
{
    /// <param name="pendingByRate">TVA encore en attente sur la facture, par taux (après avoirs et règlements précédents).</param>
    /// <param name="outstandingInclTax">TTC encore dû avant ce règlement.</param>
    /// <param name="allocatedInclTax">Part de ce règlement imputée sur la facture.</param>
    public static IReadOnlyList<VatTransfer> Share(IReadOnlyDictionary<decimal, decimal> pendingByRate, decimal outstandingInclTax, decimal allocatedInclTax)
    {
        ArgumentNullException.ThrowIfNull(pendingByRate);
        if (outstandingInclTax <= 0m || allocatedInclTax <= 0m)
        {
            return [];
        }

        bool settles = allocatedInclTax >= outstandingInclTax;
        return pendingByRate
            .Where(p => p.Value != 0m)
            .OrderByDescending(p => p.Key)
            .Select(p => new VatTransfer(p.Key, settles ? p.Value : Rounding.ToCents(p.Value * allocatedInclTax / outstandingInclTax)))
            .Where(t => t.Amount != 0m)
            .ToList();
    }
}
