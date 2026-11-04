using Lettra.Calculations;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>
/// Un devis numéroté tel que le carnet de commandes le lit : son état, ses dates, son client et son montant.
/// <paramref name="AnsweredOn"/> est le jour de la réponse du client (acceptation ou refus), s'il est connu.
/// </summary>
public sealed record QuoteFact(
    QuoteId Id,
    string Number,
    CustomerId CustomerId,
    string CustomerName,
    QuoteStatus Status,
    DateOnly IssueDate,
    DateOnly? ValidUntil,
    DateOnly? AnsweredOn,
    decimal AmountExclTax,
    decimal AmountInclTax);

/// <summary>Tranches de montant HT d'un devis (valeurs identiques à <c>QuoteAmountBandDto</c>).</summary>
public enum QuoteAmountBand
{
    /// <summary>Moins de 1 000 € HT.</summary>
    Small = 1,

    /// <summary>De 1 000 à moins de 5 000 € HT.</summary>
    Medium = 2,

    /// <summary>De 5 000 à moins de 20 000 € HT.</summary>
    Large = 3,

    /// <summary>20 000 € HT et plus.</summary>
    VeryLarge = 4,
}

/// <summary>Tranches d'ancienneté d'un devis sans réponse (valeurs identiques à <c>QuoteAgeBandDto</c>).</summary>
public enum QuoteAgeBand
{
    /// <summary>Envoyé depuis 7 jours au plus.</summary>
    FirstWeek = 1,

    /// <summary>De 8 à 15 jours.</summary>
    SecondWeek = 2,

    /// <summary>De 16 à 30 jours.</summary>
    SameMonth = 3,

    /// <summary>Plus de 30 jours (devis à validité longue).</summary>
    Older = 4,
}

/// <summary>Combien de devis conclus (acceptés, refusés ou expirés sans réponse), et combien acceptés.</summary>
public sealed record AcceptanceCount(int Accepted, int Closed)
{
    public static AcceptanceCount None { get; } = new(0, 0);

    /// <summary>Part acceptée, à 4 décimales ; <c>null</c> sans devis conclu.</summary>
    public decimal? Rate => Closed == 0 ? null : Math.Round((decimal)Accepted / Closed, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Le taux retenu pour un critère : celui observé s'il repose sur assez de devis, sinon le taux global.</summary>
public sealed record CriterionRate(AcceptanceCount Observed, decimal Rate, bool UsesGlobalRate);

/// <summary>Taux d'acceptation passé d'une tranche de montant.</summary>
public sealed record AmountBandRate(QuoteAmountBand Band, AcceptanceCount Count);

/// <summary>Taux d'acceptation passé des devis restés sans réponse au moins jusqu'à une tranche d'ancienneté.</summary>
public sealed record AgeBandRate(QuoteAgeBand Band, AcceptanceCount Count);

/// <summary>
/// Un devis en attente, pondéré : la probabilité est la moyenne de trois taux (client, tranche de montant, ancienneté),
/// le montant pondéré est le montant multiplié par cette probabilité.
/// </summary>
public sealed record WeightedQuote(
    QuoteFact Quote,
    int AgeDays,
    QuoteAmountBand AmountBand,
    QuoteAgeBand AgeBand,
    CriterionRate Customer,
    CriterionRate Amount,
    CriterionRate Age,
    decimal Probability,
    decimal WeightedExclTax,
    decimal WeightedInclTax);

/// <summary>Le carnet de commandes pondéré du jour et les taux qui l'expliquent.</summary>
public sealed record OrderBookEstimate(
    DateOnly HistoryFrom,
    AcceptanceCount Overall,
    decimal GlobalRate,
    bool HasEnoughHistory,
    IReadOnlyList<AmountBandRate> ByAmount,
    IReadOnlyList<AgeBandRate> ByAge,
    IReadOnlyList<WeightedQuote> Quotes)
{
    public decimal OpenExclTax => Quotes.Sum(q => q.Quote.AmountExclTax);

    public decimal WeightedExclTax => Quotes.Sum(q => q.WeightedExclTax);

    public decimal WeightedInclTax => Quotes.Sum(q => q.WeightedInclTax);
}

/// <summary>
/// Le carnet de commandes pondéré, sans IA : une statistique simple et explicable sur les devis des
/// <see cref="HistoryMonths"/> derniers mois. Un devis est conclu quand le client l'a accepté (puis facturé ou non) ou
/// refusé, ou quand il a expiré sans réponse (compté comme non accepté). Pour chaque devis en attente :
/// <list type="bullet">
/// <item>taux du client : ses devis conclus ;</item>
/// <item>taux de la tranche de montant : les devis conclus de la même tranche (moins de 1 000 €, 1 000 à 5 000 €,
/// 5 000 à 20 000 €, 20 000 € et plus, HT) ;</item>
/// <item>taux de l'ancienneté : parmi les devis restés sans réponse au moins aussi longtemps (début de la tranche
/// d'ancienneté du devis), la part acceptée ensuite ;</item>
/// </list>
/// un critère qui repose sur moins de <see cref="MinimumSample"/> devis prend le taux global. La probabilité est la
/// moyenne des trois. Sans assez d'historique, le taux global vaut une chance sur deux, et l'écran le dit.
/// </summary>
public static class OrderBookCalculator
{
    public const int HistoryMonths = 24;

    public const int MinimumSample = 3;

    /// <summary>Le taux global tant que moins de <see cref="MinimumSample"/> devis sont conclus : une chance sur deux.</summary>
    public const decimal NoHistoryRate = 0.5m;

    public static IReadOnlyList<QuoteAmountBand> AmountBands { get; } = Enum.GetValues<QuoteAmountBand>();

    public static IReadOnlyList<QuoteAgeBand> AgeBands { get; } = Enum.GetValues<QuoteAgeBand>();

    public static DateOnly HistoryFrom(DateOnly today) => today.AddMonths(-HistoryMonths);

    public static QuoteAmountBand AmountBandOf(decimal amountExclTax) => amountExclTax switch
    {
        < 1_000m => QuoteAmountBand.Small,
        < 5_000m => QuoteAmountBand.Medium,
        < 20_000m => QuoteAmountBand.Large,
        _ => QuoteAmountBand.VeryLarge,
    };

    public static QuoteAgeBand AgeBandOf(int ageDays) => ageDays switch
    {
        <= 7 => QuoteAgeBand.FirstWeek,
        <= 15 => QuoteAgeBand.SecondWeek,
        <= 30 => QuoteAgeBand.SameMonth,
        _ => QuoteAgeBand.Older,
    };

    /// <summary>Premier jour d'ancienneté de la tranche : 0, 8, 16 ou 31.</summary>
    public static int FirstDayOf(QuoteAgeBand band) => band switch
    {
        QuoteAgeBand.FirstWeek => 0,
        QuoteAgeBand.SecondWeek => 8,
        QuoteAgeBand.SameMonth => 16,
        _ => 31,
    };

    /// <summary>En attente de réponse : envoyé, sans réponse, encore valable (même règle que <see cref="Quote.IsPending"/>).</summary>
    public static bool IsOpen(QuoteFact quote, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return quote.Status == QuoteStatus.Validated && quote.ValidUntil is { } until && until >= today;
    }

    public static OrderBookEstimate Estimate(IEnumerable<QuoteFact> quotes, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        QuoteFact[] all = [.. quotes];
        DateOnly from = HistoryFrom(today);
        Closed[] history = [.. all.Where(q => q.IssueDate >= from && q.IssueDate <= today).Select(q => Close(q, today)).OfType<Closed>()];

        AcceptanceCount overall = Count(history);
        bool enough = overall.Closed >= MinimumSample;
        decimal global = enough ? overall.Rate!.Value : NoHistoryRate;
        AmountBandRate[] byAmount = [.. AmountBands.Select(b => new AmountBandRate(b, Count(history.Where(h => AmountBandOf(h.Fact.AmountExclTax) == b))))];
        AgeBandRate[] byAge = [.. AgeBands.Select(b => new AgeBandRate(b, Count(history.Where(h => h.DaysToClose >= FirstDayOf(b)))))];

        WeightedQuote[] open =
        [
            .. all.Where(q => IsOpen(q, today))
                .Select(q =>
                {
                    int age = Math.Max(0, today.DayNumber - q.IssueDate.DayNumber);
                    QuoteAmountBand amountBand = AmountBandOf(q.AmountExclTax);
                    QuoteAgeBand ageBand = AgeBandOf(age);
                    CriterionRate customer = Criterion(Count(history.Where(h => h.Fact.CustomerId == q.CustomerId)), global);
                    CriterionRate amount = Criterion(byAmount.First(b => b.Band == amountBand).Count, global);
                    CriterionRate ageRate = Criterion(byAge.First(b => b.Band == ageBand).Count, global);
                    decimal probability = Math.Round((customer.Rate + amount.Rate + ageRate.Rate) / 3m, 4, MidpointRounding.AwayFromZero);
                    return new WeightedQuote(
                        q, age, amountBand, ageBand, customer, amount, ageRate, probability,
                        Rounding.ToCents(q.AmountExclTax * probability), Rounding.ToCents(q.AmountInclTax * probability));
                })
                .OrderByDescending(w => w.WeightedExclTax)
                .ThenBy(w => w.Quote.Number, StringComparer.Ordinal),
        ];
        return new OrderBookEstimate(from, overall, global, enough, byAmount, byAge, open);
    }

    private static CriterionRate Criterion(AcceptanceCount observed, decimal global) =>
        observed.Closed >= MinimumSample ? new CriterionRate(observed, observed.Rate!.Value, UsesGlobalRate: false) : new CriterionRate(observed, global, UsesGlobalRate: true);

    private static AcceptanceCount Count(IEnumerable<Closed> closed)
    {
        int accepted = 0, total = 0;
        foreach (Closed quote in closed)
        {
            total++;
            accepted += quote.Accepted ? 1 : 0;
        }

        return total == 0 ? AcceptanceCount.None : new AcceptanceCount(accepted, total);
    }

    /// <summary>
    /// L'issue d'un devis, s'il est conclu. Un devis accepté ou refusé sans jour de réponse connu (repris d'une version
    /// antérieure) compte comme conclu le jour de son envoi : il pèse sur tous les taux sauf ceux d'ancienneté.
    /// </summary>
    private static Closed? Close(QuoteFact quote, DateOnly today) => quote.Status switch
    {
        QuoteStatus.Accepted or QuoteStatus.Invoiced => new Closed(quote, true, quote.AnsweredOn ?? quote.IssueDate),
        QuoteStatus.Refused => new Closed(quote, false, quote.AnsweredOn ?? quote.IssueDate),
        QuoteStatus.Validated when quote.ValidUntil is { } until && until < today => new Closed(quote, false, until),
        _ => null,
    };

    private sealed record Closed(QuoteFact Fact, bool Accepted, DateOnly ClosedOn)
    {
        public int DaysToClose => Math.Max(0, ClosedOn.DayNumber - Fact.IssueDate.DayNumber);
    }
}
