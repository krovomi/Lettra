using Lettra.Contracts.Billing;

namespace Lettra.UI.Shared.Billing;

/// <summary>
/// Ce que l'accueil affiche : les chiffres calculés par le serveur (<see cref="BillingDashboardResponse"/>) et les
/// dernières pièces émises, lues dans les listes. L'écran ne recalcule aucune règle de gestion ; il n'en tire que
/// l'évolution sur le mois précédent.
/// </summary>
public sealed record DashboardFigures(
    BillingDashboardResponse Figures,
    IReadOnlyList<InvoiceSummary> RecentInvoices,
    IReadOnlyList<QuoteSummary> RecentQuotes)
{
    public const int RecentCount = 5;

    public IReadOnlyList<MonthRevenueDto> Months => Figures.Months;

    public int InvoicesThisMonth => Figures.InvoicesThisMonth;

    public int PendingQuotes => Figures.PendingQuotes;

    public decimal PendingQuotesExclTax => Figures.PendingQuotesExclTax;

    public decimal ThisMonth => Months[^1].ExclTax;

    public decimal PreviousMonth => Months[^2].ExclTax;

    /// <summary>Évolution sur le mois précédent en pourcentage ; <c>null</c> quand le mois précédent est nul.</summary>
    public decimal? Trend => PreviousMonth == 0m ? null : Math.Round((ThisMonth - PreviousMonth) / PreviousMonth * 100m, 0, MidpointRounding.AwayFromZero);
}
