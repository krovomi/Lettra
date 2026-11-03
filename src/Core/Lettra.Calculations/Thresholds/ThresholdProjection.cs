namespace Lettra.Calculations.Thresholds;

/// <summary>Un montant daté qui compte pour un seuil : une facture, un avoir (négatif), un virement.</summary>
public readonly record struct DatedAmount(DateOnly Date, decimal Amount);

/// <summary>Où en est un seuil sur sa période : loin, à surveiller, franchissement prévu, ou déjà franchi.</summary>
public enum ThresholdStatus
{
    /// <summary>La projection reste sous <see cref="ThresholdProjection.NearRatio"/> du seuil.</summary>
    Far = 0,

    /// <summary>La projection approche du seuil sans l'atteindre d'ici la fin de la période.</summary>
    Near = 1,

    /// <summary>Au rythme actuel, le seuil sera franchi avant la fin de la période.</summary>
    Projected = 2,

    /// <summary>Le cumul de la période a déjà franchi le seuil.</summary>
    Crossed = 3,
}

/// <summary>La lecture d'un seuil : cumul à date, projection à la fin de la période, date de franchissement constatée ou prévue.</summary>
public sealed record ThresholdProgress(
    decimal Threshold,
    decimal ToDate,
    decimal Projected,
    decimal DailyRate,
    DateOnly? CrossedOn,
    DateOnly? ExpectedOn,
    ThresholdStatus Status)
{
    /// <summary>Ce que la projection dépasse du seuil à la fin de la période (zéro en dessous).</summary>
    public decimal Excess => Math.Max(0m, Projected - Threshold);
}

/// <summary>
/// Projection d'un cumul jusqu'à la fin de sa période (année civile ou exercice), en statistique simple et explicable,
/// sans IA : le cumul constaté plus le rythme des <see cref="RateDays"/> derniers jours, prolongé jusqu'au dernier jour
/// de la période. Le rythme se lit même avant le début de la période (en janvier, il vient de la fin de l'année passée).
/// Un rythme négatif (plus d'avoirs que de factures) compte pour zéro.
/// </summary>
public static class ThresholdProjection
{
    /// <summary>Le rythme se lit sur les 90 derniers jours, aujourd'hui compris.</summary>
    public const int RateDays = 90;

    /// <summary>Une projection à 80 % du seuil ou plus le met « à surveiller ».</summary>
    public const decimal NearRatio = 0.8m;

    /// <summary>
    /// Lit un seuil sur une période. <paramref name="amounts"/> doit couvrir la période jusqu'à <paramref name="today"/> et
    /// les <see cref="RateDays"/> derniers jours ; <paramref name="undated"/> compte dans le cumul dès le début de la
    /// période sans entrer dans le rythme (des sommes notées pour l'année, sans date). <paramref name="inclusive"/> :
    /// le seuil est franchi en l'atteignant (« au moins 5 000 € ») ; sinon en le dépassant (« plus de 2 400 € »).
    /// </summary>
    public static ThresholdProgress Project(
        IEnumerable<DatedAmount> amounts,
        decimal threshold,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly today,
        bool inclusive = false,
        decimal undated = 0m)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        if (periodEnd < periodStart)
        {
            throw new ArgumentOutOfRangeException(nameof(periodEnd), periodEnd, "La période finit avant de commencer.");
        }

        DateOnly asOf = today > periodEnd ? periodEnd : today;
        DatedAmount[] known = [.. amounts.Where(a => a.Date <= asOf).OrderBy(a => a.Date)];
        bool Over(decimal total) => inclusive ? total >= threshold : total > threshold;

        // Le cumul jour après jour : la date retenue est celle du dernier passage au-dessus du seuil (un avoir peut le faire redescendre).
        decimal toDate = undated;
        DateOnly? crossedOn = asOf >= periodStart && Over(toDate) ? periodStart : null;
        foreach (IGrouping<DateOnly, DatedAmount> day in known.Where(a => a.Date >= periodStart).GroupBy(a => a.Date))
        {
            bool before = Over(toDate);
            toDate += day.Sum(a => a.Amount);
            crossedOn = Over(toDate) ? (before ? crossedOn : day.Key) : null;
        }

        DateOnly rateFrom = asOf.AddDays(1 - RateDays);
        decimal dailyRate = Math.Max(0m, known.Where(a => a.Date >= rateFrom).Sum(a => a.Amount) / RateDays);
        int remaining = today > periodEnd ? 0 : periodEnd.DayNumber - asOf.DayNumber;
        decimal projected = Rounding.ToCents(toDate + (dailyRate * remaining));

        DateOnly? expectedOn = null;
        if (crossedOn is null && dailyRate > 0m && remaining > 0)
        {
            // Le premier jour où le cumul, prolongé au rythme, franchit le seuil ; au-delà de la période, rien n'est prévu.
            decimal days = (threshold - toDate) / dailyRate;
            decimal needed = Math.Max(1m, inclusive ? Math.Ceiling(days) : Math.Floor(days) + 1m);
            expectedOn = needed <= remaining ? asOf.AddDays((int)needed) : null;
        }

        ThresholdStatus status = crossedOn is not null ? ThresholdStatus.Crossed
            : expectedOn is not null ? ThresholdStatus.Projected
            : threshold > 0m && projected >= threshold * NearRatio ? ThresholdStatus.Near
            : ThresholdStatus.Far;
        return new ThresholdProgress(threshold, Rounding.ToCents(toDate), projected, dailyRate, crossedOn, expectedOn, status);
    }
}
