namespace Lettra.Contracts.Billing;

/// <summary>Chiffre d'affaires HT d'un mois : factures et acomptes validés, moins les avoirs validés.</summary>
/// <param name="Month">Premier jour du mois.</param>
/// <param name="ExclTax">Montant HT du mois, avoirs déduits.</param>
public sealed record MonthRevenueDto(DateOnly Month, decimal ExclTax);

/// <summary>
/// Les chiffres de l'accueil du dossier, calculés par le serveur : chiffre d'affaires HT des
/// <see cref="MonthCount"/> derniers mois (le mois en cours en dernier), factures émises ce mois-ci, devis en attente de
/// réponse et leur montant HT.
/// </summary>
public sealed record BillingDashboardResponse(
    IReadOnlyList<MonthRevenueDto> Months,
    int InvoicesThisMonth,
    int PendingQuotes,
    decimal PendingQuotesExclTax)
{
    public const int MonthCount = 6;
}
